using System.Collections.Concurrent;
using Jellyfin.Plugin.MetaTube.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
#if __EMBY__
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
#else
using Jellyfin.Data.Enums;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
#endif

namespace Jellyfin.Plugin.MetaTube.Helpers;

/// <summary>
/// 守住“主图指向本地图片文件”（<c>poster.jpg</c>，多片共用目录时是 <c>&lt;文件名&gt;-poster.jpg</c>）。
///
/// OrganizeMetadataTask 已经把图片落到媒体目录，但 Emby 自己的
/// “刷新元数据 / 刷新图片”仍会把 ImageInfo 改回插件的远程 URL。
/// 一旦这时断网，Emby 重新下载失败，旧图就再也对不上，海报变空白。
///
/// 这里补一层：监听条目更新事件，只要发现主图指向远程地址、
/// 而媒体目录里已有可用海报，就立刻把条目指回本地文件。
/// 启动时再扫一遍，兜住“事件没赶上（服务重启/离线期间被改）”的情况。
/// </summary>
public static class PrimaryImageGuard
{
    /// <summary>刚修过的条目在短时间内不再重复处理，避免事件回调里自我循环。</summary>
    private static readonly ConcurrentDictionary<Guid, DateTime> JustRestored = new();

    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 待补图的条目。Emby 刷新元数据时常把本地海报删掉、只留远程地址，
    /// 这种要联网重新下载，不能放在事件回调里做，统一丢给后台按顺序补。
    /// </summary>
    private static readonly ConcurrentQueue<(Guid Id, BaseItem Item, DateTime At)> Pending = new();

    private static readonly ConcurrentDictionary<Guid, byte> Queued = new();

    /// <summary>等一会儿再补：Emby 替换图片是“先删后下”，太早补可能又被删一次。</summary>
    private static readonly TimeSpan RepairGrace = TimeSpan.FromSeconds(45);

    private static readonly TimeSpan RepairInterval = TimeSpan.FromMilliseconds(150);

    private static int _workerActive;

    private static ILibraryManager _libraryManager;
    private static ILogger _logger;

    public static void Attach(ILibraryManager libraryManager, ILogger logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>订阅条目更新事件，并在启动后做一次兜底扫描。</summary>
    public static void Start()
    {
        if (_libraryManager is null)
        {
            return;
        }

        _libraryManager.ItemUpdated -= OnItemUpdated;
        _libraryManager.ItemUpdated += OnItemUpdated;

        LogInfo("Primary image guard started");

        // 不阻塞启动流程：等服务器把库加载完再扫
        _ = Task.Run(SweepAsync);
    }

    public static void Stop()
    {
        if (_libraryManager is null)
        {
            return;
        }

        _libraryManager.ItemUpdated -= OnItemUpdated;
    }

    private static void OnItemUpdated(object sender, ItemChangeEventArgs e)
    {
        var item = e?.Item;
        if (item is null)
        {
            return;
        }

        var id = item.Id;
        if (JustRestored.TryGetValue(id, out var at) && DateTime.UtcNow - at < Cooldown)
        {
            return;
        }

        try
        {
            // 本地图还在、只是指针被改回远程地址 —— 立刻改回来，不联网
            if (Repair(item))
            {
                // 只在内存里记一小段时间，超过上限直接清空，别无限增长
                if (JustRestored.Count > 512)
                {
                    JustRestored.Clear();
                }

                JustRestored[id] = DateTime.UtcNow;
                return;
            }

            // 本地图被删掉了（典型的整库“刷新元数据 + 替换图片”）—— 交给后台重新下载
            Enqueue(item);
        }
        catch (Exception e2)
        {
            LogError("Restore local primary image failed for {0}: {1}", item.Name, e2.Message);
        }
    }

    /// <summary>
    /// 把“需要重新生成海报”的条目录入队列。只收 MetaTube 刮削过的条目，
    /// 且必须真的缺图（本地没文件，或主图还是远程地址），否则不入队。
    /// </summary>
    private static void Enqueue(BaseItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Path))
        {
            return;
        }

        var pid = item.GetPid(Plugin.ProviderId);
        if (string.IsNullOrWhiteSpace(pid.Id) || string.IsNullOrWhiteSpace(pid.Provider))
        {
            return;
        }

        var posterPath = PrimaryImageStore.GetPosterPath(item);
        if (posterPath is null)
        {
            return;
        }

        var current = item.GetImageInfo(ImageType.Primary, 0);
        var needsWork = !File.Exists(posterPath) || PrimaryImageStore.IsRemotePath(current?.Path);
        if (!needsWork)
        {
            return;
        }

        if (Pending.Count > 3000 || !Queued.TryAdd(item.Id, 0))
        {
            return;
        }

        Pending.Enqueue((item.Id, item, DateTime.UtcNow));
        StartWorker();
    }

    private static void StartWorker()
    {
        if (Interlocked.CompareExchange(ref _workerActive, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(WorkerAsync);
    }

    /// <summary>
    /// 单线程按顺序补图：每条等 45 秒（避开 Emby 的删除动作），条与条之间停顿一下，
    /// 避免一次性把 MetaTube 服务端和 NAS 打满。
    /// </summary>
    private static async Task WorkerAsync()
    {
        try
        {
            while (true)
            {
                if (!Pending.TryPeek(out var next))
                {
                    return;
                }

                var wait = RepairGrace - (DateTime.UtcNow - next.At);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait > TimeSpan.FromSeconds(2) ? TimeSpan.FromSeconds(2) : wait)
                        .ConfigureAwait(false);
                    continue;
                }

                Pending.TryDequeue(out next);
                try
                {
                    await RepairMissingAsync(next.Item).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    LogError("Fill local primary image failed for {0}: {1}", next.Item?.Name, e.Message);
                }
                finally
                {
                    Queued.TryRemove(next.Id, out _);
                }

                await Task.Delay(RepairInterval).ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _workerActive, 0);

            // 收尾期间可能又有新条目进来
            if (!Pending.IsEmpty)
            {
                StartWorker();
            }
        }
    }

    /// <summary>后台补一条：按当前角标重新生成海报并指回本地。</summary>
    private static async Task RepairMissingAsync(BaseItem item)
    {
        if (item is null)
        {
            return;
        }

        var posterPath = PrimaryImageStore.GetPosterPath(item);
        if (posterPath is null)
        {
            return;
        }

        var badge = BadgeSpec.Build(BadgeSpec.HasChineseSubtitle(item), item.Path) ?? string.Empty;
        var (changed, error, url) = await PrimaryImageSync
            .EnsureAsync(item, badge, CancellationToken.None)
            .ConfigureAwait(false);

        if (!changed)
        {
            if (!string.IsNullOrEmpty(error))
            {
                LogInfo("Primary image repair skipped for {0}: {1} ({2})", item.Name, error, url);
            }

            return;
        }

        Persist(item);
        LogInfo("Fill local primary image for {0} -> {1}", item.Name, posterPath);
    }

    /// <summary>
    /// 主图被改回远程地址、而本地已有可用海报时，指回本地并保存。
    /// 返回是否真的改了。
    /// </summary>
    private static bool Repair(BaseItem item)
    {
        if (item is null)
        {
            return false;
        }

        // 只处理 MetaTube 刮削过的视频条目
        var pid = item.GetPid(Plugin.ProviderId);
        if (string.IsNullOrWhiteSpace(pid.Id) || string.IsNullOrWhiteSpace(pid.Provider))
        {
            return false;
        }

        var posterPath = PrimaryImageStore.GetPosterPath(item);
        if (posterPath is null || !File.Exists(posterPath))
        {
            return false;
        }

        // 只处理“主图被改回远程地址”这一种情况：
        // 空图（用户手动删掉）和指向其他本地文件（Emby 自己存的图）都不动 ——
        // 那些本地图断网照样能显示，覆盖它们等于撤销用户的操作。
        var current = item.GetImageInfo(ImageType.Primary, 0);
        if (current is null || !PrimaryImageStore.IsRemotePath(current.Path))
        {
            return false;
        }

        var previous = current.Path;
        PrimaryImageStore.Attach(item, posterPath);
        Persist(item);

        LogInfo("Restore local primary image for {0}: {1} -> {2}", item.Name, previous, posterPath);
        return true;
    }

    private static void Persist(BaseItem item)
    {
#if __EMBY__
        _libraryManager.UpdateItem(item, item, ItemUpdateType.ImageUpdate, null);
#else
        _libraryManager.UpdateItemAsync(item, item, ItemUpdateType.ImageUpdate, CancellationToken.None)
            .ConfigureAwait(false).GetAwaiter().GetResult();
#endif
    }

    /// <summary>
    /// 启动后扫描一遍：把“主图是远程地址、本地却有可用海报”的条目改回本地。
    /// 只读文件系统，不发网络请求，断网也能完成。
    /// </summary>
    private static async Task SweepAsync()
    {
        // 等库加载完成，避免拿到半截数据
        await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        if (_libraryManager is null)
        {
            return;
        }

        try
        {
            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                MediaTypes = new[] { MediaType.Video },
#if __EMBY__
                HasAnyProviderId = new[] { Plugin.ProviderId },
                IncludeItemTypes = new[] { nameof(Movie) }
#else
                HasAnyProviderId = new Dictionary<string, string> { { Plugin.ProviderId, string.Empty } },
                IncludeItemTypes = new[] { BaseItemKind.Movie }
#endif
            });

            var restored = 0;
            foreach (var item in items)
            {
                try
                {
                    if (Repair(item))
                    {
                        restored++;
                        continue;
                    }

                    // 本地海报缺失（被刷新删掉）的，交给后台排队重新下载
                    Enqueue(item);
                }
                catch (Exception e)
                {
                    LogError("Restore local primary image failed for {0}: {1}", item.Name, e.Message);
                }
            }

            if (restored > 0)
            {
                LogInfo("Primary image guard: restored {0} local poster(s) on startup", restored);
            }
        }
        catch (Exception e)
        {
            LogError("Primary image guard sweep failed: {0}", e.Message);
        }
    }

    private static void LogInfo(string message, params object[] args)
    {
#if __EMBY__
        _logger?.Info(message, args);
#else
        _logger?.LogInformation(message, args);
#endif
    }

    private static void LogError(string message, params object[] args)
    {
#if __EMBY__
        _logger?.Error(message, args);
#else
        _logger?.LogError(message, args);
#endif
    }
}

#if __EMBY__
public class PrimaryImageGuardEntryPoint : IServerEntryPoint
{
    public PrimaryImageGuardEntryPoint(ILibraryManager libraryManager, ILogManager logManager)
    {
        PrimaryImageGuard.Attach(libraryManager, logManager.CreateLogger<PrimaryImageGuardEntryPoint>());
    }

    public void Run()
    {
        PrimaryImageGuard.Start();
    }

    public void Dispose()
    {
        PrimaryImageGuard.Stop();
    }
}
#else
public class PrimaryImageGuardEntryPoint : IHostedService
{
    public PrimaryImageGuardEntryPoint(ILibraryManager libraryManager,
        ILogger<PrimaryImageGuardEntryPoint> logger)
    {
        PrimaryImageGuard.Attach(libraryManager, logger);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        PrimaryImageGuard.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        PrimaryImageGuard.Stop();
        return Task.CompletedTask;
    }
}

public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IHostedService, PrimaryImageGuardEntryPoint>();
    }
}
#endif
