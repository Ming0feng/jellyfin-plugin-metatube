using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.MetaTube.Helpers;

/// <summary>
/// 无码/破解检出：按文件名分词匹配标记（-U、-UC、-AI、_restored、_wm 等）。
/// </summary>
public static class UncensoredDetector
{
    /// <summary>
    /// 内置默认清单，始终参与检出：用户配置里保存的通常是旧默认值，
    /// 只依赖配置会让新出现的 AI 修复/破解标记漏判。
    /// </summary>
    private const string DefaultKeywords =
        "U,UC,AI,uncensored,无码,破解,LTX,wm,restored,jasna,cracked,remastered,decensored,leaked,nomosaic";

    private static readonly Regex Separator = new(@"[-_\s.@\[\]()（）]+", RegexOptions.Compiled);

    /// <summary>文件名是否带无码/破解标记。</summary>
    public static bool HasUncensored(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var filename = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(filename))
            return false;

        var tokens = Separator.Split(filename)
            .Where(token => token.Length > 0)
            .ToArray();

        foreach (var keyword in GetKeywords())
        {
            if (keyword.All(c => c < 128))
            {
                // 分隔符分词匹配：SONE-263-U、ABC-123-UC、SSNI-830_restored_LTX_wm
                if (tokens.Contains(keyword, StringComparer.OrdinalIgnoreCase))
                    return true;

                // 多字符关键字支持直接后缀：abc123uncensored
                if (keyword.Length > 1 && filename.EndsWith(keyword, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (filename.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                // 中文关键字按包含匹配：无码、破解、无码破解
                return true;
            }
        }

        return false;
    }

    private static string[] GetKeywords()
    {
        var raw = string.Join(',',
            DefaultKeywords,
            Plugin.Instance?.Configuration?.UncensoredKeywords ?? string.Empty);

        return raw.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(keyword => keyword.Trim())
            .Where(keyword => keyword.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
