using System;
using System.Linq;

namespace Jellyfin.Plugin.MetaTube.Helpers;

/// <summary>
/// 刮削搜索词的规范化。
///
/// Emby/Jellyfin 会把文件名末尾的标记原样带进条目名，例如
/// `STARS-979-LTX`、`SSIS-942-U`、`ABC-123 中文字幕`。
/// 把这些标记一起丢给服务端会导致所有源都识别不出番号，
/// 所以搜索前先按配置的关键字把末尾标记去掉。
/// </summary>
public static class SearchKeyword
{
    private static readonly char[] Separators = { '-', '_', '.', ' ' };

    /// <summary>
    /// 去掉名称末尾的无码/破解与中文字幕标记。
    /// 没配置关键字、或名称本来就不带标记时原样返回。
    /// </summary>
    public static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var keywords = GetSuffixKeywords();
        if (keywords.Length == 0)
        {
            return name.Trim();
        }

        var result = name.Trim();
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
            config?.UncensoredKeywords ?? string.Empty,
            config?.ChineseSubtitleKeywords ?? string.Empty);

        return raw.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(keyword => keyword.Trim())
            .Where(keyword => keyword.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
