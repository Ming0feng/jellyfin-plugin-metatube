using System.Collections.Concurrent;
using System.Net.Http;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MetaTube.Helpers;

/// <summary>
/// 把主图落到媒体目录，并让条目始终引用本地文件。
///
/// 为什么需要它：插件的图片地址是指向 MetaTube 的远程 URL，
/// Emby 只有在“下载成功的那一次”才有图。一旦角标参数变化，
/// 整理任务会重设 URL，Emby 便重新下载；此时若外网/代理不通，
/// 旧图会被判成“对不上新地址”而不再使用 —— 表现就是海报凭空消失。
///
/// 这里改成：下载 → 校验 → 写 <c>poster.jpg</c> → 条目指向这个文件。
/// 下载失败时保持原样（旧图继续能看），宁可角标是旧的，也不要整张图消失。
/// </summary>
public static class PrimaryImageStore
{
    private const string PosterFileName = "poster.jpg";

    /// <summary>角标指纹：记录这张 poster.jpg 是按哪组角标生成的（写在文件旁边）。</summary>
    private const string StampSuffix = ".metatube";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>目录里“不同片名”的数量的短期缓存，避免每次条目更新都去列目录（NAS 上很慢）。</summary>
    private static readonly ConcurrentDictionary<string, (int Count, DateTime At)> DirectoryTitleCounts = new();

    private static readonly TimeSpan DirectoryCacheTtl = TimeSpan.FromMinutes(5);

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".strm", ".mp4", ".mkv", ".iso", ".ts", ".m2ts", ".avi", ".wmv", ".mov",
        ".mpg", ".mpeg", ".flv", ".webm", ".rmvb", ".vob", ".m4v", ".3gp", ".f4v"
    };

    /// <summary>
    /// 这个条目该用的本地主图路径；无法确定目录时返回 null。
    ///
    /// 目录里只有这一部片时用 <c>poster.jpg</c>；目录里塞了多部不同的片时，
    /// <c>poster.jpg</c> 是“整个文件夹”的图（也可能是别的片的），
    /// 必须改成 <c>&lt;文件名&gt;-poster.jpg</c>，否则同一目录下的片子会共用同一张海报，
    /// 甚至把文件夹图片覆盖掉。
    /// </summary>
    public static string GetPosterPath(BaseItem item)
    {
        var mediaPath = item.Path;
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(mediaPath);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        if (!IsSharedDirectory(directory))
        {
            return Path.Combine(directory, PosterFileName);
        }

        var name = Path.GetFileNameWithoutExtension(mediaPath);
        return string.IsNullOrEmpty(name)
            ? Path.Combine(directory, PosterFileName)
            : Path.Combine(directory, name + "-poster.jpg");
    }

    /// <summary>
    /// 目录里是否放着“多部不同的片”。同一部片有多个来源文件（<c>a.mp4</c> + <c>a.strm</c>、
    /// <c>xxx.com@a.strm</c> + <c>a.strm</c>）仍算一部，照旧用 <c>poster.jpg</c>。
    /// </summary>
    private static bool IsSharedDirectory(string directory)
    {
        if (DirectoryTitleCounts.TryGetValue(directory, out var cached) &&
            DateTime.UtcNow - cached.At < DirectoryCacheTtl)
        {
            return cached.Count > 1;
        }

        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (!VideoExtensions.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                titles.Add(NormalizeTitle(Path.GetFileNameWithoutExtension(file)));
                if (titles.Count > 1)
                {
                    break;
                }
            }
        }
        catch (Exception)
        {
            // 目录读不到就按“只有一部片”处理，保持原有行为
            titles.Clear();
            titles.Add(string.Empty);
        }

        DirectoryTitleCounts[directory] = (titles.Count, DateTime.UtcNow);
        return titles.Count > 1;
    }

    /// <summary>
    /// 把文件名压成“片名”：去掉 <c>xxx.com@</c> 这类站点前缀，
    /// 让同一部片的不同来源文件落到同一个名字上。
    /// </summary>
    private static string NormalizeTitle(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var at = name.LastIndexOf('@');
        return (at >= 0 && at < name.Length - 1 ? name.Substring(at + 1) : name).Trim();
    }

    /// <summary>
    /// 本地文件已存在、角标指纹一致、且条目正指向它 —— 什么都不用做。
    /// 这是断网时能保持稳定的关键：命中就直接跳过网络请求。
    /// </summary>
    public static bool IsFresh(BaseItem item, string posterPath, string badge)
    {
        if (!File.Exists(posterPath))
        {
            return false;
        }

        var stampPath = posterPath + StampSuffix;
        var stamp = File.Exists(stampPath) ? File.ReadAllText(stampPath).Trim() : string.Empty;
        if (!string.Equals(stamp, (badge ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var current = item.GetImageInfo(ImageType.Primary, 0);
        return current != null && IsSameFile(current.Path, posterPath);
    }

    /// <summary>图片路径是否为远程地址（http/https）。</summary>
    public static bool IsRemotePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>两个路径是否指向同一个文件（忽略大小写与相对路径差异）。</summary>
    public static bool IsSameFile(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // 路径里有非法字符（比如远程 URL）时按“不是同一个文件”处理
            return false;
        }
    }

    /// <summary>把条目指向本地文件（不改动图片内容）。</summary>
    public static void Attach(BaseItem item, string posterPath)
    {
        item.SetImage(new ItemImageInfo
        {
            Path = posterPath,
            Type = ImageType.Primary,
            DateModified = File.GetLastWriteTimeUtc(posterPath)
        }, 0);
    }

    /// <summary>
    /// 下载图片。失败时返回 <c>(null, 原因)</c> —— 调用方据此保持原样，
    /// 绝不把错误响应写成 poster.jpg；原因会进日志，便于事后排查。
    /// </summary>
    public static async Task<(byte[] Data, string Error)> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return (null, $"HTTP {(int)response.StatusCode}");
            }

            // MetaTube 出错时返回的是 application/json，不能当图片存下来
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return (null, $"unexpected content-type \"{mediaType}\"");
            }

            var data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return LooksLikeImage(data)
                ? (data, null)
                : (null, $"not an image ({data.Length} bytes)");
        }
        catch (Exception e)
        {
            // 超时、DNS、代理失效……都算“这次拿不到”
            return (null, $"{e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// 覆盖写入 poster.jpg 并记录角标指纹。写临时文件再替换，避免留下半张图。
    /// </summary>
    public static bool Save(string posterPath, byte[] data, string badge)
    {
        try
        {
            var temp = posterPath + ".tmp";
            File.WriteAllBytes(temp, data);
            File.Move(temp, posterPath, true);
            File.WriteAllText(posterPath + StampSuffix, (badge ?? string.Empty).Trim());
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool LooksLikeImage(byte[] data)
    {
        if (data == null || data.Length < 4)
        {
            return false;
        }

        // JPEG / PNG / GIF / WEBP
        return (data[0] == 0xFF && data[1] == 0xD8) ||
               (data[0] == 0x89 && data[1] == 0x50) ||
               (data[0] == 0x47 && data[1] == 0x49) ||
               (data[0] == 0x52 && data[1] == 0x49); // RIFF (webp)
    }
}
