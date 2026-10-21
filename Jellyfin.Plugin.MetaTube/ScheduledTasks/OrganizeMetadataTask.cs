using Jellyfin.Plugin.MetaTube.Extensions;
using Jellyfin.Plugin.MetaTube.Helpers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
#if __EMBY__
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Logging;

#else
using MediaBrowser.Controller.Sorting;
using Microsoft.Extensions.Logging;
using Jellyfin.Data.Enums;
#endif

namespace Jellyfin.Plugin.MetaTube.ScheduledTasks;

public class OrganizeMetadataTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger _logger;

#if __EMBY__
    public OrganizeMetadataTask(ILogManager logManager, ILibraryManager libraryManager)
    {
        _logger = logManager.CreateLogger<OrganizeMetadataTask>();
        _libraryManager = libraryManager;
    }
#else
    public OrganizeMetadataTask(ILogger<OrganizeMetadataTask> logger, ILibraryManager libraryManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
    }
#endif

    public string Key => $"{Plugin.ProviderName}OrganizeMetadata";

    public string Name => "Organize Metadata";

    public string Description => $"Organizes video metadata provided by {Plugin.ProviderName} in library.";

    public string Category => Plugin.ProviderName;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
#if __EMBY__
            Type = TaskTriggerInfo.TriggerDaily,
#else
            Type = TaskTriggerInfoType.DailyTrigger,
#endif
            TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
        };
    }

#if __EMBY__
    public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
#else
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
#endif
    {
        await Task.Yield();

        progress?.Report(0);

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            MediaTypes = new[] { MediaType.Video },
#if __EMBY__
            HasAnyProviderId = new[] { Plugin.ProviderId },
            IncludeItemTypes = new[] { nameof(Movie) },
#else
            HasAnyProviderId = new Dictionary<string, string> { { Plugin.ProviderId, string.Empty } },
            IncludeItemTypes = new[] { BaseItemKind.Movie }
#endif
        }).ToList();

        foreach (var (idx, item) in items.WithIndex())
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((double)idx / items.Count * 100);

            var genres = item.Genres?.ToList() ?? new List<string>();

            var imageUpdated = false;
            try
            {
                var hasChineseSubtitle = ChineseSubtitleDetector.HasChineseSubtitle(item.Path);

                // Add `ChineseSubtitle` genre.
                if (hasChineseSubtitle && !genres.Contains(ChineseSubtitle))
                {
                    genres.Add(ChineseSubtitle);
                }
                // Remove `ChineseSubtitle` genre.
                else if (!hasChineseSubtitle && genres.Contains(ChineseSubtitle))
                {
                    genres.RemoveAll(s => s.Equals(ChineseSubtitle));
                }

                // 主图落到媒体目录（带不带角标都做）。
                //
                // 以前这里只比对 URL 上的 badge 参数、不一致就重设远程地址，
                // 结果是：Emby 必须重新下载，而下载失败（断网/代理挂掉）时
                // 旧图又因为“地址对不上”被弃用，海报直接变空白。
                // 现在由 SetPrimaryImage 负责“下载 → 写 poster.jpg → 指向本地”，
                // 拿不到新图就保留旧图，不再出现整张丢失。
                var badge = BadgeSpec.Build(hasChineseSubtitle, item.Path) ?? string.Empty;
                imageUpdated = await SetPrimaryImage(item, badge, cancellationToken);
                if (imageUpdated)
                    _logger.Info("Update primary image for video: {0}", item.Name);
            }
            catch (Exception e)
            {
                _logger.Error("Update ChineseSubtitle for video {0}: {1}", item.Name, e.Message);
            }

            // Remove duplicates.
            var orderedGenres =
                (Plugin.Instance.Configuration.EnableGenreSubstitution
                    // Substitute genres.
                    ? Plugin.Instance.Configuration.GetGenreSubstitutionTable().Substitute(genres)
                    : genres).Distinct().OrderByString(genre => genre).ToList();

            // Skip updating item if equal, unless the primary image was just repaired.
            if (!imageUpdated &&
                (!orderedGenres.Any() ||
                 (item.Genres?.SequenceEqual(orderedGenres, StringComparer.OrdinalIgnoreCase)).GetValueOrDefault(false)))
                continue;

            if (orderedGenres.Any())
                item.Genres = orderedGenres.ToArray();

            _logger.Info("Organize metadata for video: {0}", item.Name);

#if __EMBY__
            _libraryManager.UpdateItem(item, item, ItemUpdateType.MetadataEdit, null);
#else
            await _libraryManager
                .UpdateItemAsync(item, item, ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);
#endif
        }

        progress?.Report(100);
    }

    #region Helper

    private const string ChineseSubtitle = "中文字幕";

    /// <summary>
    /// 确保媒体目录里有一张可用的 poster.jpg，并让条目指向它。
    ///
    /// 三种情况：
    /// 1. 本地已有、角标指纹一致 → 什么都不做（断网时保持稳定）；
    /// 2. 能下载到新图 → 覆盖 poster.jpg，条目指向本地文件；
    /// 3. 下载失败 → 只要本地还有旧图就指回它，宁可角标是旧的，也不要空白。
    /// </summary>
    private async Task<bool> SetPrimaryImage(BaseItem item, string badge, CancellationToken cancellationToken)
    {
        var pid = item.GetPid(Plugin.ProviderId);
        if (string.IsNullOrWhiteSpace(pid.Id) || string.IsNullOrWhiteSpace(pid.Provider))
            return false;

        var posterPath = PrimaryImageStore.GetPosterPath(item);
        if (posterPath == null)
            return false;

        if (PrimaryImageStore.IsFresh(item, posterPath, badge))
            return false;

        byte[] data = null;
        try
        {
            var m = await ApiClient.GetMovieInfoAsync(pid.Provider, pid.Id, cancellationToken);
            var url = ApiClient.GetPrimaryImageApiUrl(
                m.Provider, m.Id, pid.Position ?? -1,
                string.IsNullOrEmpty(badge) ? null : badge);
            var (downloaded, error) = await PrimaryImageStore.DownloadAsync(url, cancellationToken);
            data = downloaded;
            if (data == null)
            {
                _logger.Info("Primary image download failed for {0}: {1} ({2})",
                    item.Name, error, url);
            }
        }
        catch (Exception e)
        {
            // 元数据都取不到（断网）—— 下面统一兜底到本地旧图
            _logger.Info("Get movie info failed for {0}: {1}", item.Name, e.Message);
        }

        if (data == null)
        {
            if (File.Exists(posterPath))
            {
                PrimaryImageStore.Attach(item, posterPath);
                return true;
            }

            return false;
        }

        if (!PrimaryImageStore.Save(posterPath, data, badge))
            return false;

        PrimaryImageStore.Attach(item, posterPath);
        return true;
    }

    #endregion
}