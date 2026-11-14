using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.MetaTube.Helpers;

/// <summary>
/// 多版本合并时，根据影片本地文件名生成版本标签。
///
/// 规则：
/// - 命中中文字幕关键字 → 「内嵌中文」
/// - 命中 LTX / restored → 「LTX无码」
/// - 命中其余无码破解关键字 → 「无码」
/// - 都没有 → 「原版」
/// - 组合用「-」连接、中文在前：-UC → 内嵌中文-无码；-LTX-C → 内嵌中文-LTX无码
/// </summary>
public static class VersionName
{
    private static readonly string[] LtxMarkers = { "ltx", "restored" };

    private static readonly Regex Separator = new(@"[-_\s.@\[\]()（）]+", RegexOptions.Compiled);

    /// <summary>根据文件路径生成版本标签。</summary>
    public static string Build(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "原版";

        var filename = Path.GetFileNameWithoutExtension(path);

        var hasChinese = ChineseSubtitleDetector.HasEmbeddedChineseSubtitle(filename);
        var uncensored = DetectUncensored(filename);

        var parts = new List<string>();
        if (hasChinese)
            parts.Add("内嵌中文");
        if (uncensored != null)
            parts.Add(uncensored);

        return parts.Count == 0 ? "原版" : string.Join("-", parts);
    }

    /// <summary>排序权重：LTX无码 &gt; 无码 &gt; 内嵌中文 &gt; 原版。</summary>
    public static int Rank(string versionName)
    {
        return versionName switch
        {
            "LTX无码" => 3,
            "无码" => 2,
            "内嵌中文" => 1,
            _ => 0
        };
    }

    private static string DetectUncensored(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return null;

        var tokens = Separator.Split(filename)
            .Where(token => token.Length > 0)
            .ToArray();

        // LTX / restored 专属标签。
        foreach (var marker in LtxMarkers)
        {
            if (tokens.Contains(marker, StringComparer.OrdinalIgnoreCase))
                return "LTX无码";
        }

        // 其余无码破解关键字（复用角标同一套清单）。
        return UncensoredDetector.HasUncensored(filename) ? "无码" : null;
    }
}
