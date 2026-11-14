using System.ComponentModel;

namespace Jellyfin.Plugin.MetaTube.Configuration;

/// <summary>
/// 多版本合并偏好。
/// </summary>
public enum MergeScope
{
    [Description("同目录合并")]
    SameFolder,

    [Description("各媒体库合并")]
    LibraryScope,

    [Description("跨媒体库合并")]
    GlobalScope
}
