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
                var config = Plugin.Instance.Configuration;
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

                // Badges follow the current detection result; this also restores the
                // badges dropped by a metadata/image refresh.
                if (config.EnableBadges || config.EnableUncensoredBadges)
                {
                    var badge = BadgeSpec.Build(hasChineseSubtitle, item.Path);
                    if (!BadgeSpec.Matches(item.GetImageInfo(ImageType.Primary, 0)?.Path, badge))
                    {
                        imageUpdated = await SetPrimaryImage(item, badge ?? string.Empty, cancellationToken);
                        if (imageUpdated)
                            _logger.Info("Update badge for video: {0}", item.Name);
                    }
                }
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

    private static async Task<bool> SetPrimaryImage(BaseItem item, string badge, CancellationToken cancellationToken)
    {
        var pid = item.GetPid(Plugin.ProviderId);
        if (string.IsNullOrWhiteSpace(pid.Id) || string.IsNullOrWhiteSpace(pid.Provider))
            return false;

        var m = await ApiClient.GetMovieInfoAsync(pid.Provider, pid.Id, cancellationToken);
        // Set first primary image.
        item.SetImage(new ItemImageInfo
        {
            Path = ApiClient.GetPrimaryImageApiUrl(m.Provider, m.Id, pid.Position ?? -1, badge),
            Type = ImageType.Primary
        }, 0);
        return true;
    }

    #endregion
}