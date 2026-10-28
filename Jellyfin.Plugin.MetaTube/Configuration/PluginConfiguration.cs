using Jellyfin.Plugin.MetaTube.Helpers;
using Jellyfin.Plugin.MetaTube.Translation;
#if __EMBY__
using System.ComponentModel;
using Emby.Web.GenericEdit;
using MediaBrowser.Model.Attributes;

#else
using MediaBrowser.Model.Plugins;
#endif

namespace Jellyfin.Plugin.MetaTube.Configuration;

#if __EMBY__
public class PluginConfiguration : EditableOptionsBase
{
    public override string EditorTitle => Plugin.ProviderName;
#else
public class PluginConfiguration : BasePluginConfiguration
{
#endif

#if __EMBY__
    [DisplayName("服务器地址")]
    [Description("MetaTube 服务端的完整地址，建议使用 HTTPS 协议。")]
    [Required]
#endif
    public string Server { get; set; } = string.Empty;

#if __EMBY__
    [DisplayName("访问令牌")]
    [Description("MetaTube 服务端的访问令牌；若后端未设置令牌则留空。")]
#endif
    public string Token { get; set; } = string.Empty;

#if __EMBY__
    [DisplayName("启用自动更新")]
    [Description("通过计划任务自动更新插件。")]
    public bool EnableAutoUpdate { get; set; } = true;
#endif

#if __EMBY__
    [DisplayName("启用合集")]
    [Description("按系列自动创建合集。")]
#endif
    public bool EnableCollections { get; set; } = false;

#if __EMBY__
    [DisplayName("启用导演信息")]
    [Description("为对应影片的元数据添加导演信息。")]
#endif
    public bool EnableDirectors { get; set; } = true;

#if __EMBY__
    [DisplayName("启用评分")]
    [Description("显示来源网站的社区评分。")]
#endif
    public bool EnableRatings { get; set; } = true;

#if __EMBY__
    [DisplayName("启用预告片")]
    [Description("生成 strm 格式的在线预告片。")]
#endif
    public bool EnableTrailers { get; set; } = false;

#if __EMBY__
    [DisplayName("启用真实演员名")]
    [Description("使用 AVBASE 中的真实演员名进行搜索与替换。")]
#endif
    public bool EnableRealActorNames { get; set; } = false;

#if __EMBY__
    [DisplayName("启用角标")]
    [Description("在主图上添加中文字幕角标。")]
#endif
    public bool EnableBadges { get; set; } = false;

#if __EMBY__
    [DisplayName("角标图片地址")]
    [Description("自定义角标图片地址，建议使用 PNG 格式。（默认：zimu.png）")]
#endif
    public string BadgeUrl { get; set; } = "zimu.png";

#if __EMBY__
    [DisplayName("中文字幕关键字")]
    [Description("文件名末尾出现这些关键字时视为已内嵌中文字幕并添加角标，英文逗号分隔。默认：C,UC,ch,中文字幕")]
#endif
    public string ChineseSubtitleKeywords { get; set; } = "C,UC,ch,中文字幕";

#if __EMBY__
    [DisplayName("外挂中文字幕语言码")]
    [Description("同目录外挂字幕文件的语言码，英文逗号分隔。默认：chs,cht,chi,zh,zh-cn,zh-tw,zho")]
#endif
    public string ExternalChineseSubtitleLanguages { get; set; } = "chs,cht,chi,zh,zh-cn,zh-tw,zho";

#if __EMBY__
    [DisplayName("启用无码破解角标")]
    [Description("在主图右上角添加“无码破解”角标（需要服务端支持 uncensored.png 角标）。")]
#endif
    public bool EnableUncensoredBadges { get; set; } = false;

#if __EMBY__
    [DisplayName("无码破解关键字")]
    [Description("文件名末尾出现这些关键字时视为无码/破解并添加角标，英文逗号分隔。默认：U,UC,AI,uncensored,无码,破解,LTX")]
#endif
    public string UncensoredKeywords { get; set; } = "U,UC,AI,uncensored,无码,破解,LTX";

#if __EMBY__
    [DisplayName("无码破解角标图片")]
    [Description("无码破解角标图片名称，需服务端内置。（默认：uncensored.png）")]
#endif
    public string UncensoredBadgeUrl { get; set; } = "uncensored.png";

#if __EMBY__
    [DisplayName("主图比例")]
    [Description("主图的宽高比，设为负值则使用默认值。")]
#endif
    public double PrimaryImageRatio { get; set; } = -1;

#if __EMBY__
    [DisplayName("默认图片质量")]
    [Description("JPEG 图片的默认压缩质量，取值范围 0~100。（默认：90）")]
    [MinValue(0)]
    [MaxValue(100)]
    [Required]
#endif
    public int DefaultImageQuality { get; set; } = 90;

#if __EMBY__
    [DisplayName("启用影片源过滤")]
    [Description("过滤并排序来自各影片源的搜索结果。")]
#endif
    public bool EnableMovieProviderFilter { get; set; } = false;

#if __EMBY__
    [DisplayName("影片源过滤")]
    [Description(
        "源名称不区分大小写，越靠左优先级越高，多个源用英文逗号分隔。")]
#endif
    public string RawMovieProviderFilter
    {
        get => _movieProviderFilter?.Any() == true ? string.Join(',', _movieProviderFilter) : string.Empty;
        set => _movieProviderFilter = value?.Split(',').Select(s => s.Trim()).Where(s => s.Any())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public List<string> GetMovieProviderFilter()
    {
        return _movieProviderFilter;
    }

    private List<string> _movieProviderFilter;

#if __EMBY__
    [DisplayName("启用模板")]
#endif
    public bool EnableTemplate { get; set; } = false;

#if __EMBY__
    [DisplayName("名称模板")]
#endif
    public string NameTemplate { get; set; } = DefaultNameTemplate;

#if __EMBY__
    [DisplayName("标语模板")]
#endif
    public string TaglineTemplate { get; set; } = DefaultTaglineTemplate;

    public static string DefaultNameTemplate => "{number} {title}";

    public static string DefaultTaglineTemplate => "配信開始日 {date}";

#if __EMBY__
    [DisplayName("翻译模式")]
#endif
    public TranslationMode TranslationMode { get; set; } = TranslationMode.Disabled;

#if __EMBY__
    [DisplayName("翻译引擎")]
#endif
    public TranslationEngine TranslationEngine { get; set; } = TranslationEngine.Baidu;

#if __EMBY__
    [DisplayName("百度 App ID")]
    [VisibleCondition(nameof(TranslationEngine), ValueCondition.IsEqual, TranslationEngine.Baidu)]
#endif
    public string BaiduAppId { get; set; } = string.Empty;

#if __EMBY__
    [DisplayName("百度 App Key")]
    [VisibleCondition(nameof(TranslationEngine), ValueCondition.IsEqual, TranslationEngine.Baidu)]
#endif
    public string BaiduAppKey { get; set; } = string.Empty;

#if __EMBY__
    [DisplayName("谷歌 API Key")]
    [VisibleCondition(nameof(TranslationEngine), ValueCondition.IsEqual, TranslationEngine.Google)]
#endif
    public string GoogleApiKey { get; set; } = string.Empty;

#if __EMBY__
    [DisplayName("谷歌 API 地址")]
    [Description("自定义谷歌翻译 API 地址。（可选）")]
    [VisibleCondition(nameof(TranslationEngine), ValueCondition.IsEqual, TranslationEngine.Google)]
#endif
    public string GoogleApiUrl { get; set; } = string.Empty;

#if __EMBY__
    [DisplayName("DeepL API Key")]
    [VisibleCondition(nameof(TranslationEngine), ValueCondition.IsEqual, TranslationEngine.DeepL)]
#endif
    public string DeepLApiKey { get; set; } = string.Empty;

#if __EMBY__
    [DisplayName("DeepL API 地址")]
    [Description("兼容 DeepL 的自定义 API 地址。（可选）")]
    [VisibleCondition(nameof(TranslationEngine), ValueCondition.IsEqual, TranslationEngine.DeepL)]
#endif
    public string DeepLApiUrl { get; set; } = string.Empty;

#if __EMBY__
    [DisplayName("OpenAI API Key")]
    [VisibleCondition(nameof(TranslationEngine), ValueCondition.IsEqual, TranslationEngine.OpenAi)]
#endif
    public string OpenAiApiKey { get; set; } = string.Empty;

#if __EMBY__
    [DisplayName("OpenAI API 地址")]
    [Description("兼容 OpenAI 的自定义 API 地址，需包含版本路径（例如 https://api.openai.com/v1）。（可选）")]
    [VisibleCondition(nameof(TranslationEngine), ValueCondition.IsEqual, TranslationEngine.OpenAi)]
#endif
    public string OpenAiApiUrl { get; set; } = string.Empty;

#if __EMBY__
    [DisplayName("OpenAI 模型")]
    [Description("留空时自动从 API 地址的 /v1/models 中选取首个可用对话模型；填写后优先使用填写的模型。")]
    [VisibleCondition(nameof(TranslationEngine), ValueCondition.IsEqual, TranslationEngine.OpenAi)]
#endif
    public string OpenAiModel { get; set; } = string.Empty;

#if __EMBY__
    [DisplayName("启用标题替换")]
#endif
    public bool EnableTitleSubstitution { get; set; } = false;

#if __EMBY__
    [DisplayName("标题替换表")]
    [Description(
        "每行一条记录，用等号分隔；等号右侧留空表示删除左侧内容。")]
    [EditMultiline(5)]
#endif
    public string TitleRawSubstitutionTable
    {
        get => _titleSubstitutionTable?.ToString();
        set => _titleSubstitutionTable = SubstitutionTable.Parse(value);
    }

    public SubstitutionTable GetTitleSubstitutionTable()
    {
        return _titleSubstitutionTable;
    }

    private SubstitutionTable _titleSubstitutionTable;

#if __EMBY__
    [DisplayName("启用演员替换")]
#endif
    public bool EnableActorSubstitution { get; set; } = false;

#if __EMBY__
    [DisplayName("演员替换表")]
    [Description(
        "每行一条记录，用等号分隔；等号右侧留空表示删除该演员。")]
    [EditMultiline(5)]
#endif
    public string ActorRawSubstitutionTable
    {
        get => _actorSubstitutionTable?.ToString();
        set => _actorSubstitutionTable = SubstitutionTable.Parse(value);
    }

    public SubstitutionTable GetActorSubstitutionTable()
    {
        return _actorSubstitutionTable;
    }

    private SubstitutionTable _actorSubstitutionTable;

#if __EMBY__
    [DisplayName("启用类别替换")]
#endif
    public bool EnableGenreSubstitution { get; set; } = false;

#if __EMBY__
    [DisplayName("类别替换表")]
    [Description(
        "每行一条记录，用等号分隔；等号右侧留空表示删除该类别。")]
    [EditMultiline(5)]
#endif
    public string GenreRawSubstitutionTable
    {
        get => _genreSubstitutionTable?.ToString();
        set => _genreSubstitutionTable = SubstitutionTable.Parse(value);
    }

    public SubstitutionTable GetGenreSubstitutionTable()
    {
        return _genreSubstitutionTable;
    }

    private SubstitutionTable _genreSubstitutionTable;
}