using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.MetaTube.Configuration;
using Jellyfin.Plugin.MetaTube.Extensions;
using Jellyfin.Plugin.MetaTube.Helpers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
#if __EMBY__
using MediaBrowser.Model.Logging;
#else
using Microsoft.Extensions.Logging;
using Jellyfin.Data.Enums;
#endif

namespace Jellyfin.Plugin.MetaTube.MultiVersion;

/// <summary>
/// 电影多版本合并：把番号相同的电影条目合并为一个多版本条目。
/// </summary>
public class MovieMerger
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger _logger;

    private static readonly object SyncLock = new();

    // 媒体库缓存：VirtualFolderInfo 直接带 Locations（路径）与 LibraryOptions，
    // 不依赖 GetItemList 返回对象的懒加载父级信息（ParentId/GetParents 不可靠）。
    private IReadOnlyList<VirtualFolderInfo> _libraries;

#if __EMBY__
    public MovieMerger(ILogManager logManager, ILibraryManager libraryManager)
    {
        _logger = logManager.CreateLogger<MovieMerger>();
        _libraryManager = libraryManager;
    }
#else
    public MovieMerger(ILogger logger, ILibraryManager libraryManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
    }
#endif

    /// <summary>按文件路径查找条目并触发合并（供刮削钩子调用）。</summary>
    public void MergeByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        // 刷新流程保存元数据的耗时不定（含翻译/图片），合并若赶在保存之前，
        // 会被后续保存重置版本关系。这里循环：合并 → 等待 → 验证进组 → 未进组重试。
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var movie = _libraryManager.GetItemList(new InternalItemsQuery { Path = path })
                .OfType<Movie>()
                .FirstOrDefault();
            if (movie == null)
                return;

            var number = MovieNumber.Normalize(movie.Path);
            if (number == null)
                return;

            // MergeForItem 内部幂等：已进组直接返回。
            MergeForItem(movie);

            // 已进组（或无需合并）则停止。
            if (IsInVersionGroup(movie, number))
                return;

            Thread.Sleep(4000);
        }
    }

    /// <summary>条目是否已与同番号的其他条目同处一个版本组（即合并已生效）。</summary>
    private bool IsInVersionGroup(Movie movie, string number)
    {
        var group = QueryCandidates(movie, Plugin.Instance.Configuration.MergePreference)
            .Where(c => c.Id != movie.Id)
            .Where(IsRealMedia)
            .Where(c => MovieNumber.Normalize(c.Path) == number)
            .ToList();

        // 没有同番号的其他条目 → 无需合并，视为已完成。
        if (group.Count == 0)
            return true;

        return group.Any(c => string.Equals(c.PresentationUniqueKey,
            movie.PresentationUniqueKey, StringComparison.Ordinal));
    }

    /// <summary>刮削成功后，对单个条目做合并检查。</summary>
    public void MergeForItem(Movie item)
    {
        var config = Plugin.Instance.Configuration;
        if (!config.EnableAutoMerge)
            return;

        // Emby 会为 strm 所在文件夹创建空壳条目（Path 指向目录），不参与合并。
        if (!IsRealMedia(item))
            return;

        var number = MovieNumber.Normalize(item.Path);
        if (number == null)
            return;

        lock (SyncLock)
        {
            // 已是版本组主条目则跳过。
            if (IsPrimary(item))
                return;

            var candidates = QueryCandidates(item, config.MergePreference);
            var group = candidates
                .Where(c => c.Id != item.Id)
                .Where(IsRealMedia)
                .Where(c => MovieNumber.Normalize(c.Path) == number)
                .ToList();

            if (group.Count == 0)
                return;

            // 幂等：item 已与组内条目同处一个版本组（PresentationUniqueKey 一致）
            // 说明完整合并过，跳过，避免反复洗牌。
            var myKey = item.PresentationUniqueKey;
            if (!string.IsNullOrEmpty(myKey) && group.Any(c =>
                    string.Equals(c.PresentationUniqueKey, myKey, StringComparison.Ordinal)))
                return;

            // 主版本 = 当前刚刮削的条目，其余（含旧组主条目）一并洗牌为替代版本。
            Merge(OrderVersions(item, group), number);
        }
    }

    /// <summary>全库合并（计划任务）：按合并偏好分组后批量合并。</summary>
    public void MergeAll(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance.Configuration;
        if (!config.EnableAutoMerge)
            return;

        var all = QueryAllMovies();
        var withNumber = all.Where(IsRealMedia)
            .Where(m => MovieNumber.Normalize(m.Path) != null).ToList();
        var enabled = withNumber.Where(IsMetatubeEnabled).ToList();
        _logger.Info("MergeAll: total={0} withNumber={1} enabled={2}",
            all.Count, withNumber.Count, enabled.Count);

        var groups = enabled
            .GroupBy(m => (ScopeKey(m, config.MergePreference), MovieNumber.Normalize(m.Path)))
            .Where(g => g.Count() > 1)
            // 版本键已统一的组 = 完整合并过，跳过。
            .Where(g => g.Select(m => m.PresentationUniqueKey).Distinct().Count() > 1)
            .ToList();
        _logger.Info("MergeAll: groups to merge={0}", groups.Count);

        var total = groups.Count;
        var idx = 0;
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            idx++;
            progress?.Report((double)(idx - 1) / total * 100);

            lock (SyncLock)
            {
                // 整组洗牌：不管组内有几个旧版本组，统一并成一个。
                var primary = group.OrderByDescending(m => m.DateLastSaved).First();
                var others = group.Where(m => m.Id != primary.Id).ToList();
                Merge(OrderVersions(primary, others), MovieNumber.Normalize(primary.Path));
            }
        }

        progress?.Report(100);
    }

    /// <summary>是否为真实媒体文件条目（排除 Emby 为 strm 文件夹建的空壳条目）。</summary>
    private static bool IsRealMedia(BaseItem item)
    {
        return !string.IsNullOrEmpty(item.Path) && !Directory.Exists(item.Path);
    }

    private List<Movie> QueryCandidates(Movie item, MergeScope scope)
    {
        var query = BuildQuery();
        switch (scope)
        {
            case MergeScope.SameFolder:
                if (!string.IsNullOrEmpty(item.ContainingFolderPath))
                {
#if __EMBY__
                    query.PathStartsWith = item.ContainingFolderPath;
#else
                    query.ParentId = item.ParentId;
#endif
                }
                break;

            case MergeScope.LibraryScope:
                var top = GetLibrary(item);
#if __EMBY__
                if (top != null && long.TryParse(top.ItemId, out var libId))
                    query.TopParentIds = new[] { libId };
#else
                if (top != null && Guid.TryParse(top.ItemId, out var libGuid))
                    query.TopParentIds = new[] { libGuid };
#endif
                break;
        }

        return _libraryManager.GetItemList(query).OfType<Movie>().ToList();
    }

    private List<Movie> QueryAllMovies()
    {
        return _libraryManager.GetItemList(BuildQuery()).OfType<Movie>().ToList();
    }

    private static InternalItemsQuery BuildQuery()
    {
        return new InternalItemsQuery
        {
            MediaTypes = new[] { MediaType.Video },
#if __EMBY__
            IncludeItemTypes = new[] { nameof(Movie) }
#else
            IncludeItemTypes = new[] { BaseItemKind.Movie }
#endif
        };
    }

    private string ScopeKey(Movie movie, MergeScope scope)
    {
        return scope switch
        {
            MergeScope.SameFolder => movie.ContainingFolderPath ?? string.Empty,
            MergeScope.LibraryScope => GetLibrary(movie)?.ItemId ?? string.Empty,
            _ => string.Empty
        };
    }

    /// <summary>获取条目所属的顶层媒体库（按路径前缀匹配，最具体者优先）。</summary>
    private VirtualFolderInfo GetLibrary(BaseItem item)
    {
        var path = item.Path;
        if (string.IsNullOrEmpty(path))
            return null;

        // 按路径前缀匹配，取最具体（最长路径）的库。
        return GetLibraries()
            .Where(l => l.Locations != null)
            .SelectMany(l => l.Locations.Select(loc => new { Lib = l, Loc = loc }))
            .Where(x => !string.IsNullOrEmpty(x.Loc) &&
                        path.StartsWith(x.Loc, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Loc.Length)
            .Select(x => x.Lib)
            .FirstOrDefault();
    }

    /// <summary>查询所有顶层媒体库（CollectionFolder 且 IsTopParent）。</summary>
    private IReadOnlyList<VirtualFolderInfo> GetLibraries()
    {
        if (_libraries != null)
            return _libraries;

        _libraries = _libraryManager.GetVirtualFolders();
        _logger.Info("MergeAll: libraries={0}", _libraries.Count);

        return _libraries;
    }

    private static bool IsPrimary(Movie item)
    {
#if __EMBY__
        return item is Video video && video.GetAlternateVersionIds().Count > 0;
#else
        return item is Video video && video.HasLocalAlternateVersions;
#endif
    }

    private bool IsMetatubeEnabled(Movie item)
    {
        try
        {
            var library = GetLibrary(item);
            if (library == null)
                return false;

            var options = library.LibraryOptions;
            if (options == null)
                return true;

            var typeOptions = options.GetTypeOptions(nameof(Movie));

            // 库未自定义 Movie 的 metadata fetcher 列表时，Emby 默认启用全部已注册
            // fetcher（含 MetaTube），视为启用。只有显式列出了 fetcher 却不含
            // MetaTube 时，才判定为未启用。
            if (typeOptions?.MetadataFetchers == null || typeOptions.MetadataFetchers.Length == 0)
                return true;

            return typeOptions.MetadataFetchers
                .Contains(Plugin.ProviderName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e)
        {
            _logger.Error("Check MetaTube library enabled for {0} error: {1}", item.Name, e.Message);
            return false;
        }
    }

    private static List<Movie> OrderVersions(Movie primary, IEnumerable<Movie> others)
    {
        var ordered = new List<Movie> { primary };
        ordered.AddRange(others.OrderByDescending(m => VersionName.Rank(VersionName.Build(m.Path))));
        return ordered;
    }

    private void Merge(List<Movie> versions, string number)
    {
        var ids = string.Join(",", versions.Select(v => v.Id));
        _logger.Info("Merge {0} versions for {1}: {2}", versions.Count, number, ids);

#if __EMBY__
        _libraryManager.MergeItems(versions.Cast<BaseItem>().ToArray());
#else
        var primary = versions[0];
        foreach (var version in versions.Skip(1))
        {
            if (version is Video video)
            {
                video.SetPrimaryVersionId(primary.Id.ToString());
                _libraryManager.UpdateItemAsync(video, video, ItemUpdateType.MetadataEdit, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
        }
#endif
    }
}
