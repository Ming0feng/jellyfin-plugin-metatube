using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.MetaTube.Helpers;

/// <summary>
/// 中文字幕检出：文件名关键字（included 内嵌）与同目录外挂字幕文件。
/// </summary>
public static class ChineseSubtitleDetector
{
    private const string DefaultKeywords = "C,UC,ch,中文字幕";
    private const string DefaultLanguages = "chs,cht,chi,zh,zh-cn,zh-tw,zho";
    private const string SubtitleExtensions = "ass|srt|ssa|smi|sub|idx|psb|vtt";

    /// <summary>按文件路径判断是否带中文字幕。</summary>
    public static bool HasChineseSubtitle(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return HasEmbeddedChineseSubtitle(Path.GetFileNameWithoutExtension(path)) ||
               HasExternalChineseSubtitle(path);
    }

    /// <summary>文件名中出现中文字幕关键字（如 SONE-263-UC、cjod288ch）。</summary>
    public static bool HasEmbeddedChineseSubtitle(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return false;

        foreach (var keyword in GetKeywords())
        {
            if (keyword.All(c => c < 128))
            {
                // 分隔符分词匹配：SONE-263-UC、ACHJ-081-C
                if (HasTag(filename, keyword))
                    return true;

                // 多字符关键字支持直接后缀：cjod288ch
                if (keyword.Length > 1 && filename.EndsWith(keyword, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (filename.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                // 中文关键字按包含匹配：中文字幕、中字
                return true;
            }
        }

        return false;
    }

    /// <summary>同目录存在与视频同名的中文外挂字幕文件。</summary>
    public static bool HasExternalChineseSubtitle(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
                return false;

            return HasExternalChineseSubtitle(Path.GetFileNameWithoutExtension(path),
                Directory.GetFiles(parent).Select(Path.GetFileName));
        }
        catch (Exception)
        {
            // 网络路径不可达等情况一律视为无字幕，避免影响元数据刷新。
            return false;
        }
    }

    private static bool HasExternalChineseSubtitle(string basename, IEnumerable<string> files)
    {
        var languages = (Plugin.Instance?.Configuration?.ExternalChineseSubtitleLanguages ?? string.Empty)
            .Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(language => language.Trim())
            .Where(language => language.Length > 0)
            .Select(Regex.Escape)
            .ToArray();

        if (languages.Length == 0)
            languages = DefaultLanguages.Split(',');

        var r = new Regex($@"\.({string.Join("|", languages)})\.({SubtitleExtensions})$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        return files.Any(name => !string.IsNullOrWhiteSpace(name) &&
                                 r.IsMatch(name) &&
                                 r.Replace(name, string.Empty)
                                     .Equals(basename, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] GetKeywords()
    {
        var raw = Plugin.Instance?.Configuration?.ChineseSubtitleKeywords;
        if (string.IsNullOrWhiteSpace(raw))
            raw = DefaultKeywords;

        return raw.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(keyword => keyword.Trim())
            .Where(keyword => keyword.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool HasTag(string filename, string tag)
    {
        return Regex.Split(filename, @"[-_\s]").Contains(tag, StringComparer.OrdinalIgnoreCase);
    }
}
