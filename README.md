<h1 align="center">Jellyfin Plugin MetaTube</h1>
<p align="center"><b>English | <a href="README.ja.md">日本語</a></b></p>

> **本仓库是 [metatube-community/jellyfin-plugin-metatube](https://github.com/metatube-community/jellyfin-plugin-metatube) 的个人维护分支。**
> 在上游基础上补充了角标组合、无码/中文字幕检出以及翻译链路改造，详见下方[本地新增功能](#本地新增功能)。

<p align="center">
<img alt="Plugin Banner" src="https://metatube-community.github.io/images/banner-dark.png"/>
<br/>
<br/>

<a href="https://github.com/metatube-community/jellyfin-plugin-metatube/actions">
<img alt="GitHub Workflow Status" src="https://img.shields.io/github/actions/workflow/status/metatube-community/jellyfin-plugin-metatube/dotnetcore.yml?branch=main&logo=github">
</a>
<a href="https://github.com/metatube-community/jellyfin-plugin-metatube/search?l=c%23">
<img alt="GitHub top language" src="https://img.shields.io/github/languages/top/metatube-community/jellyfin-plugin-metatube?color=%23239120&label=.NET&logo=csharp">
</a>
<a href="https://github.com/metatube-community/jellyfin-plugin-metatube/blob/main/LICENSE">
<img alt="License" src="https://img.shields.io/github/license/metatube-community/jellyfin-plugin-metatube">
</a>
<a href="https://github.com/metatube-community/jellyfin-plugin-metatube">
<img alt="gitHub Stars" src="https://img.shields.io/github/stars/metatube-community/jellyfin-plugin-metatube?style=flat">
</a>
<a href="https://github.com/metatube-community/jellyfin-plugin-metatube">
<img alt="Downloads" src="https://img.shields.io/github/downloads/metatube-community/jellyfin-plugin-metatube/total">
</a>
<a href="https://github.com/metatube-community/jellyfin-plugin-metatube/releases">
<img alt="Releases" src="https://img.shields.io/github/v/release/metatube-community/jellyfin-plugin-metatube?include_prereleases&logo=smartthings">
</a>
</p>

## About

MetaTube Plugin for Jellyfin/Emby.

## Features

- Full Metadata: Including title, overview, genres, director, actors, and studio.
- Full Search: Support searching for movies and actors across various providers.
- Trailer Video: Support trailers without downloading the full trailer videos.
- Scheduled Task: Automatically organize metadata genres and update plugin.
- Face Detection: Cut primary image with face centered by face detection engine.
- Auto Translation: Support translate certain metadata to preferred language.

## Platforms

[![Jellyfin](https://img.shields.io/static/v1?color=%2300A4DC&style=for-the-badge&label=Jellyfin&logo=jellyfin&message=10.11.x)](https://jellyfin.org/)
[![Emby](https://img.shields.io/static/v1?color=%2352B54B&style=for-the-badge&label=Emby&logo=emby&message=4.9.x)](https://emby.media/)

_NOTE: This project will only support stable versions._

## Documentation

- [Plugin installation](https://metatube-community.github.io/wiki/plugin-installation/)
- [Server deployment](https://metatube-community.github.io/wiki/server-deployment/)
- [File naming rules](https://metatube-community.github.io/wiki/naming-rules/)
- [Auto translation](https://metatube-community.github.io/wiki/auto-translation/)
- [Build from source](https://metatube-community.github.io/wiki/build-from-source/)
- [Metadata providers](https://metatube-community.github.io/wiki/metadata-providers/)

Full documentation and examples can be found at [Wiki](https://metatube-community.github.io/wiki/).

## Community

Welcome and feel free to ask any questions at [Discussions](https://github.com/metatube-community/jellyfin-plugin-metatube/discussions).

## 本地新增功能

以下功能为个人分支新增，上游原版不包含。

### 角标组合

主图可同时叠加两个角标：左上「中文字幕」、右上「无码破解」。服务端的 badge 参数以逗号分隔，`tr:` 前缀表示右上角，例如 `zimu.png,tr:uncensored.png`。角标图片地址、无码关键字均可配置。

### 中文字幕检出

判断条目是否已有中文字幕，命中任一条件即视为有：

- 类型（Genre）中包含「中文字幕」
- 文件名带分隔符标记：`-C`、`-UC`、`-ch`、`中文字幕`
- 同目录存在 `ass/srt/ssa/smi/sub/idx/psb/vtt` 外挂字幕

### 无码 / 破解检出

按文件名分词匹配关键字 `U`、`UC`、`AI`、`uncensored`、`无码`、`破解`，例如 `SONE-263-U`、`ABC-123-UC`、`XYZ-AI`，关键字列表可自定义。

### 翻译链路改造

| 改动 | 说明 |
| --- | --- |
| 长文本分段 | 超过 600 字符的文本按句末标点/`<br>` 边界切分后逐段翻译再合并，避免单次请求过长被拒 |
| 硬超时 | 免费翻译单次请求 20 秒硬超时（含内部重试），不再把刷新拖到分钟级 |
| 自动降级 | 免费翻译超时、429、5xx 等任意失败都会自动改用 OpenAI 兼容引擎重试一次 |
| 模型自动探测 | OpenAI 模型留空时，自动请求 `{api-url}/models` 选取首个可用对话模型并记录日志 |
| 字段级容错 | 标题与简介独立处理，任一字段失败只保留该字段原文，不影响另一个 |
| 取消语义修复 | 重试逻辑不再吞掉 `OperationCanceledException`，服务关闭时可立即中断 |

## Licence

This plugin is released under the [MIT](https://github.com/metatube-community/jellyfin-plugin-metatube/blob/main/LICENSE) License.

## Star History

[![Star History Chart](https://api.star-history.com/svg?repos=metatube-community/jellyfin-plugin-metatube&type=Date)](https://star-history.com/#metatube-community/jellyfin-plugin-metatube&Date)
