using Jellyfin.Plugin.MetaTube.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MetaTube.Helpers;

/// <summary>
/// 让条目的主图落到本地海报文件：本地已有就直接指过去，没有就下载补上。
/// 整理任务（全量）和“刷新之后自动补图”（按条目）共用这一份逻辑。
/// </summary>
internal static class PrimaryImageSync
{
    /// <summary>
    /// 确保条目指向本地海报。
    /// 返回是否改动了条目，以及失败原因 / 使用的图片地址（便于调用方打日志）。
    /// </summary>
    public static async Task<(bool Changed, string Error, string Url)> EnsureAsync(
        BaseItem item, string badge, CancellationToken cancellationToken)
    {
        if (item is null)
        {
            return (false, "no item", null);
        }

        var posterPath = PrimaryImageStore.GetPosterPath(item);
        if (posterPath is null)
        {
            return (false, "no media directory", null);
        }

        // 本地已有按当前角标生成的海报：只修指针，不联网
        if (PrimaryImageStore.MatchesStamp(posterPath, badge))
        {
            var existing = item.GetImageInfo(ImageType.Primary, 0);
            if (existing is not null && PrimaryImageStore.IsSameFile(existing.Path, posterPath))
            {
                return (false, null, null);
            }

            PrimaryImageStore.Attach(item, posterPath);
            return (true, null, null);
        }

        var pid = item.GetPid(Plugin.ProviderId);
        if (string.IsNullOrWhiteSpace(pid.Id) || string.IsNullOrWhiteSpace(pid.Provider))
        {
            return (false, "no provider id", null);
        }

        string url;
        try
        {
            var m = await ApiClient.GetMovieInfoAsync(pid.Provider, pid.Id, cancellationToken)
                .ConfigureAwait(false);
            url = ApiClient.GetPrimaryImageApiUrl(
                m.Provider, m.Id, pid.Position ?? -1,
                string.IsNullOrEmpty(badge) ? null : badge);
        }
        catch (Exception e)
        {
            // 元数据取不到（断网 / 服务端不通）
            return (false, $"get movie info: {e.Message}", null);
        }

        var (data, error) = await PrimaryImageStore.DownloadAsync(url, cancellationToken).ConfigureAwait(false);
        if (data is null)
        {
            return (false, error, url);
        }

        if (!PrimaryImageStore.Save(posterPath, data, badge))
        {
            return (false, "write poster failed", url);
        }

        PrimaryImageStore.Attach(item, posterPath);
        return (true, null, url);
    }
}
