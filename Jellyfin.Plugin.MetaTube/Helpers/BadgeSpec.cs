using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.MetaTube.Helpers;

/// <summary>
/// 主图角标参数：把“中文字幕”（左上）与“无码破解”（右上）组合成服务端的 badge 参数。
/// 语法为逗号分隔的列表，每项可用 <c>tr:</c> 前缀指定右上角，例如 <c>zimu.png,tr:uncensored.png</c>。
/// </summary>
public static class BadgeSpec
{
    private const string ChineseSubtitle = "中文字幕";
    private const string TopRightPrefix = "tr:";
    private const string DefaultChineseBadge = "zimu.png";
    private const string DefaultUncensoredBadge = "uncensored.png";

    /// <summary>条目是否有中文字幕（类型标记或文件名/同目录字幕）。</summary>
    public static bool HasChineseSubtitle(BaseItem item)
    {
        return item.Genres?.Any(genre => genre.Equals(ChineseSubtitle, StringComparison.OrdinalIgnoreCase)) == true ||
               ChineseSubtitleDetector.HasChineseSubtitle(item.Path);
    }

    /// <summary>按当前配置拼出条目应使用的角标参数，没有角标时返回空串。</summary>
    public static string Build(bool hasChineseSubtitle, string path)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
            return string.Empty;

        var parts = new List<string>();

        if (config.EnableBadges && hasChineseSubtitle)
            parts.Add(string.IsNullOrWhiteSpace(config.BadgeUrl) ? DefaultChineseBadge : config.BadgeUrl);

        if (config.EnableUncensoredBadges && UncensoredDetector.HasUncensored(path))
            parts.Add(TopRightPrefix + (string.IsNullOrWhiteSpace(config.UncensoredBadgeUrl)
                ? DefaultUncensoredBadge
                : config.UncensoredBadgeUrl));

        return string.Join(",", parts);
    }

    /// <summary>取出图片地址里的 badge 参数值（已解码）。</summary>
    public static string GetBadge(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return string.Empty;

        var queryStart = imagePath.IndexOf('?');
        if (queryStart < 0 || queryStart == imagePath.Length - 1)
            return string.Empty;

        foreach (var pair in imagePath.Substring(queryStart + 1).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
                continue;

            if (pair.Substring(0, separator).Equals("badge", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair.Substring(separator + 1));
        }

        return string.Empty;
    }

    /// <summary>图片地址上的角标是否与预期一致。</summary>
    public static bool Matches(string imagePath, string expected)
    {
        return string.Equals(GetBadge(imagePath), expected ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
