using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.MetaTube.Helpers;

/// <summary>
/// 番号提取与归一化，用于多版本合并时判断两个条目是否同一部作品。
///
/// 归一化规则：去掉数字发行前缀（259LUXU-1795 → LUXU-1795）、
/// 去掉横线/下划线/点、转大写。这样 SSNI-830 / ssni830 / 259LUXU-1795 /
/// LUXU-1795 会分别归一化为 SSNI830 / SSNI830 / LUXU1795 / LUXU1795。
/// </summary>
public static class MovieNumber
{
    // 数字发行前缀：259LUXU-1795 → LUXU-1795；300MIUM-973 → MIUM-973。
    private static readonly Regex AmateurPrefix =
        new(@"(?i)^(259|300|200|261|345)(?=[a-z]{2,})", RegexOptions.Compiled);

    // 无横线紧凑形：ssni830 → ssni830；cjod288ch → cjod288。
    private static readonly Regex CompactNumber =
        new(@"(?i)(?<!\d)([a-z]{2,}\d{3,5})(?!\d)", RegexOptions.Compiled);

    // 分卷标记：KAVR-531-part1 / soe-385-part2 / xx-cd1 / xx-disc2 是同一部的不同部分，
    // 不是「多版本」，不参与合并。
    private static readonly Regex PartSuffix =
        new(@"(?i)(?:^|[-_.\s])(?:part|cd|disc)\d+([-_.\s]|$)", RegexOptions.Compiled);

    /// <summary>从文件路径提取番号并归一化；抽不到返回 null。</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var filename = StripPrefix(Path.GetFileNameWithoutExtension(path));

        // 分卷文件不参与多版本合并。
        if (PartSuffix.IsMatch(filename))
            return null;

        string number;
        var match = SearchKeyword.MovieNumberRegex.Match(filename);
        if (match.Success)
        {
            number = match.Groups[1].Value;
        }
        else
        {
            var compact = CompactNumber.Match(filename);
            if (!compact.Success)
                return null;
            number = compact.Groups[1].Value;
        }

        number = AmateurPrefix.Replace(number, string.Empty);
        number = Regex.Replace(number, @"[-_.]", string.Empty);
        return number.ToUpperInvariant();
    }

    /// <summary>去掉文件名里的站点/域名前缀：489155.com@xxx、[22sht.me]xxx、hhd800.com@xxx。</summary>
    private static string StripPrefix(string filename)
    {
        var at = filename.LastIndexOf('@');
        if (at >= 0)
            filename = filename[(at + 1)..];

        var bracket = filename.LastIndexOf(']');
        if (bracket >= 0)
            filename = filename[(bracket + 1)..];

        return filename;
    }
}
