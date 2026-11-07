using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.MetaTube.Helpers;

/// <summary>
/// 从文件名提取刮削用的番号。
///
/// 番号解析是客户端职责：优先用真实文件路径，把宿主编排的条目名或带
/// AI 修复标记的文件名收敛成一个纯番号再发给服务端。只有抽不到标准番号时
/// 才把清理后的原串交给服务端自己的清洗逻辑兜底。
/// </summary>
public static class SearchKeyword
{
    private static readonly char[] Separators = { '-', '_', '.', ' ' };

    /// <summary>
    /// 高置信度番号：字母-数字（SSNI-830）、带发行数字前缀（259LUXU-1795）、
    /// 六位日期号（123456-789）。刻意不覆盖无横线 / maker 前缀 / 域名前缀等
    /// 冷门格式，避免误抽，那些交给服务端兜底。图片跨源回退也复用这份定义。
    /// </summary>
    public static readonly Regex MovieNumberRegex = new(
        @"(?i)(?<!\d)(\d{2,4}[a-z]{2,}[-_]\d{2,5}|[a-z]{2,}[-_]\d{2,5}|\d{4,6}[-_]\d{2,3})(?![a-z\d])",
        RegexOptions.Compiled);

    /// <summary>
    /// 内置关键字。配置里保存的往往是旧默认值，把标记内置进来才能保证
    /// 新出现的 AI 修复 / 无码破解后缀始终被剥离，而不依赖用户改配置。
    /// </summary>
    private const string BuiltInKeywords =
        "U,UC,AI,uncensored,无码,破解,LTX,wm,restored,jasna,cracked,remastered,decensored,leaked,C,ch,中文字幕";

    /// <summary>
    /// AI 去码 / 修复 / 破解尾巴的起点（兜底清理用）。
    /// </summary>
    private static readonly Regex CrackTailRegex = new(
        @"(?i)(?:^|[-_.@\s])(?:jasna|ltx|wm|restored|remaster(?:ed)?|crack(?:ed)?|decensor(?:ed)?|uncensored|leak(?:ed)?|no[-_.]?mosaic|nomosaic|\d+(?:\.\d+){1,})(?:[-_.@\s]|$)",
        RegexOptions.Compiled);

    public static string Normalize(string name) => Normalize(name, null);

    /// <summary>
    /// 提取番号，path 优先于 name；抽不到时返回清理后的原串。
    /// </summary>
    public static string Normalize(string name, string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            var number = ExtractNumber(Path.GetFileNameWithoutExtension(path));
            if (number != null)
            {
                return number;
            }
        }

        var fromName = ExtractNumber(name);
        if (fromName != null)
        {
            return fromName;
        }

        // 冷门格式（FC2、carib/1pondo、无横线番号、域名前缀等）不做猜测，
        // 只去掉破解/字幕后缀后交给服务端 number.Trim 兜底。
        var result = string.IsNullOrWhiteSpace(name)
            ? (string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFileNameWithoutExtension(path))
            : name.Trim();
        return CleanSuffixes(result);
    }

    private static string ExtractNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var match = MovieNumberRegex.Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// 去掉番号之后的无码/破解/AI 修复标记，以及末尾的中文字幕标记。
    /// </summary>
    private static string CleanSuffixes(string result)
    {
        if (string.IsNullOrEmpty(result))
        {
            return result;
        }

        var marker = CrackTailRegex.Match(result);
        if (marker.Success && marker.Index > 0)
        {
            var trimmed = result[..marker.Index].TrimEnd();
            if (trimmed.Length > 0)
            {
                result = trimmed;
            }
        }

        var keywords = GetSuffixKeywords();
        bool stripped;
        do
        {
            stripped = false;
            foreach (var keyword in keywords)
            {
                // 分隔符后缀：STARS-979-LTX、SSIS-942_U
                foreach (var separator in Separators)
                {
                    var suffix = separator + keyword;
                    if (result.Length > suffix.Length &&
                        result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        result = result[..^suffix.Length].TrimEnd();
                        stripped = true;
                    }
                }

                // 无分隔符后缀：abc123uncensored（仅长关键字，避免误伤）
                if (keyword.Length >= 3 &&
                    result.Length > keyword.Length &&
                    result.EndsWith(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    result = result[..^keyword.Length].TrimEnd();
                    stripped = true;
                }
            }
        } while (stripped);

        return result;
    }

    private static string[] GetSuffixKeywords()
    {
        var config = Plugin.Instance?.Configuration;
        var raw = string.Join(',',
            BuiltInKeywords,
            config?.UncensoredKeywords ?? string.Empty,
            config?.ChineseSubtitleKeywords ?? string.Empty);

        return raw.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(keyword => keyword.Trim())
            .Where(keyword => keyword.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
