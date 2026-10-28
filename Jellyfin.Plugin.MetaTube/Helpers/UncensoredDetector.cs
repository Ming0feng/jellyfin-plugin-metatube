using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.MetaTube.Helpers;

/// <summary>
/// 无码/破解检出：按文件名后缀关键字判断（-U、-UC、-AI、无码、破解等）。
/// </summary>
public static class UncensoredDetector
{
    private const string DefaultKeywords = "U,UC,AI,uncensored,无码,破解,LTX";

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
                // 分隔符分词匹配：SONE-263-U、ABC-123-UC、XYZ-AI
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
        var raw = Plugin.Instance?.Configuration?.UncensoredKeywords;
        if (string.IsNullOrWhiteSpace(raw))
            raw = DefaultKeywords;

        return raw.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(keyword => keyword.Trim())
            .Where(keyword => keyword.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
