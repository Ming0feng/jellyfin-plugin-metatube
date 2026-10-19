using System.Text.RegularExpressions;
using Jellyfin.Plugin.MetaTube.Extensions;
using Jellyfin.Plugin.MetaTube.Helpers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
#if __EMBY__
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Logging;

#else
using Microsoft.Extensions.Logging;
#endif

namespace Jellyfin.Plugin.MetaTube.Providers;

public class MovieImageProvider : BaseProvider, IRemoteImageProvider, IHasOrder
{
#if __EMBY__
    public MovieImageProvider(ILogManager logManager) : base(logManager.CreateLogger<MovieImageProvider>())
#else
    public MovieImageProvider(ILogger<MovieImageProvider> logger) : base(logger)
#endif
    {
    }

#if __EMBY__
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, LibraryOptions libraryOptions,
        CancellationToken cancellationToken)
#else
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
#endif
    {
        var pid = item.GetPid(Plugin.ProviderId);
        if (string.IsNullOrWhiteSpace(pid.Id) || string.IsNullOrWhiteSpace(pid.Provider))
            return Enumerable.Empty<RemoteImageInfo>();

        var badge = GetBadge(item);
        var images = new List<RemoteImageInfo>();

        // The stored provider comes first, then the best cross-provider match
        // as a fallback when the stored one cannot provide images.
        foreach (var candidate in await GetImageCandidates(item, pid, cancellationToken))
        {
            try
            {
                var m = await ApiClient.GetMovieInfoAsync(candidate.Provider, candidate.Id,
                    cancellationToken);
                var position = candidate.Position ?? -1;
                images.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Type = ImageType.Primary,
                    Url = ApiClient.GetPrimaryImageApiUrl(m.Provider, m.Id, position, badge)
                });
                images.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Type = ImageType.Thumb,
                    Url = ApiClient.GetThumbImageApiUrl(m.Provider, m.Id)
                });
                images.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Type = ImageType.Backdrop,
                    Url = ApiClient.GetBackdropImageApiUrl(m.Provider, m.Id)
                });

                foreach (var imageUrl in m.PreviewImages ?? Enumerable.Empty<string>())
                {
                    images.Add(new RemoteImageInfo
                    {
                        ProviderName = Name,
                        Type = ImageType.Primary,
                        Url = ApiClient.GetPrimaryImageApiUrl(m.Provider, m.Id, imageUrl, position, badge: badge)
                    });
                    images.Add(new RemoteImageInfo
                    {
                        ProviderName = Name,
                        Type = ImageType.Thumb,
                        Url = ApiClient.GetThumbImageApiUrl(m.Provider, m.Id, imageUrl)
                    });
                    images.Add(new RemoteImageInfo
                    {
                        ProviderName = Name,
                        Type = ImageType.Backdrop,
                        Url = ApiClient.GetBackdropImageApiUrl(m.Provider, m.Id, imageUrl)
                    });
                }
            }
            catch (Exception e)
            {
                Logger.Warn("Get images for {0} failed: {1}", candidate.ToString(), e.Message);
            }
        }

        return images;
    }

    /// <summary>
    /// Builds the image source candidates. When the provider filter is enabled
    /// the sources follow its priority order (left = highest); otherwise the
    /// stored provider comes first and the best other source is the fallback.
    /// </summary>
    private async Task<List<ProviderId>> GetImageCandidates(BaseItem item, ProviderId pid,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ProviderId>();

        var keyword = GetSearchKeyword(item, pid);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            try
            {
                // Empty provider => server searches all providers concurrently.
                var results = await ApiClient.SearchMovieAsync(keyword, string.Empty, cancellationToken);
                if (results != null)
                {
                    if (Configuration.EnableMovieProviderFilter &&
                        Configuration.GetMovieProviderFilter() is { Count: > 0 } filter)
                    {
                        // Follow the configured priority order.
                        var ordered = results
                            .Where(r => !string.IsNullOrWhiteSpace(r.Provider) &&
                                        !string.IsNullOrWhiteSpace(r.Id))
                            .Where(r => filter.Contains(r.Provider, StringComparer.OrdinalIgnoreCase))
                            .OrderBy(r => filter.FindIndex(s =>
                                string.Equals(s, r.Provider, StringComparison.OrdinalIgnoreCase)));
                        foreach (var result in ordered)
                        {
                            if (AddCandidate(candidates, result.Provider, result.Id, pid.Position))
                                Logger.Info("Image source: {0} => {1}:{2}", keyword, result.Provider, result.Id);
                        }
                    }

                    if (candidates.Count == 0)
                    {
                        // Keep the stored provider first, then the best other source.
                        AddCandidate(candidates, pid.Provider, pid.Id, pid.Position);
                        foreach (var result in results)
                        {
                            if (AddCandidate(candidates, result.Provider, result.Id, pid.Position))
                            {
                                Logger.Info("Fallback image source: {0} => {1}:{2}",
                                    keyword, result.Provider, result.Id);
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Warn("Cross-provider image search failed: {0}", e.Message);
            }
        }

        // Always include the stored provider as the last resort.
        AddCandidate(candidates, pid.Provider, pid.Id, pid.Position);
        return candidates;
    }

    private static bool AddCandidate(List<ProviderId> candidates, string provider, string id, double? position)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(id))
            return false;
        if (candidates.Any(c =>
                string.Equals(c.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)))
            return false;

        candidates.Add(new ProviderId
        {
            Provider = provider,
            Id = id,
            Position = position
        });
        return true;
    }

    /// <summary>
    /// Builds the keyword for cross-provider search. The stored id is used
    /// when it already looks like a movie number, otherwise the number is
    /// extracted from the item name.
    /// </summary>
    private static string GetSearchKeyword(BaseItem item, ProviderId pid)
    {
        if (!string.IsNullOrWhiteSpace(pid.Id) && MovieNumberRegex.IsMatch(pid.Id))
            return pid.Id;

        if (!string.IsNullOrWhiteSpace(item.Name))
        {
            var match = MovieNumberRegex.Match(item.Name);
            if (match.Success)
                return match.Groups[1].Value;
        }

        return pid.Id;
    }

    private static readonly Regex MovieNumberRegex =
        new(@"(?i)([a-z]{2,}[-_]\d{2,}|\d{4,6}[-_]\d{2,3})", RegexOptions.Compiled);

    /// <summary>
    /// 仅当条目确实带中文字幕时才让图片地址带上角标，
    /// 这样刷新元数据并替换图片后角标不会丢失，也不会给无字幕条目误加角标。
    /// </summary>
    private static string GetBadge(BaseItem item)
    {
        var badge = BadgeSpec.Build(BadgeSpec.HasChineseSubtitle(item), item.Path);
        return string.IsNullOrEmpty(badge) ? null : badge;
    }

    public bool Supports(BaseItem item)
    {
        return item is Movie;
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return new List<ImageType>
        {
            ImageType.Primary,
            ImageType.Thumb,
            ImageType.Backdrop
        };
    }
}