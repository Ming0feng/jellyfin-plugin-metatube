using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MetaTube.MultiVersion;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
#if __EMBY__
using MediaBrowser.Model.Logging;
#else
using Microsoft.Extensions.Logging;
#endif

namespace Jellyfin.Plugin.MetaTube.ScheduledTasks;

/// <summary>
/// 全库合并计划任务：按合并偏好把番号相同的电影合并为多版本，用于补齐历史条目。
/// </summary>
public class MergeMultiVersionsTask : IScheduledTask
{
    private readonly MovieMerger _movieMerger;

#if __EMBY__
    public MergeMultiVersionsTask(ILogManager logManager, ILibraryManager libraryManager)
    {
        _movieMerger = new MovieMerger(logManager, libraryManager);
    }
#else
    public MergeMultiVersionsTask(ILogger<MergeMultiVersionsTask> logger, ILibraryManager libraryManager)
    {
        _movieMerger = new MovieMerger(logger, libraryManager);
    }
#endif

    public string Key => $"{Plugin.ProviderName}MergeMultiVersions";

    public string Name => "Merge Multi Versions of Metatube";

    public string Description =>
        $"Merges movies with the same number into multi-version items according to the merge preference.";

    public string Category => Plugin.ProviderName;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
#if __EMBY__
            Type = TaskTriggerInfo.TriggerDaily,
#else
            Type = TaskTriggerInfoType.DailyTrigger,
#endif
            TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
        };
    }

#if __EMBY__
    public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
#else
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
#endif
    {
        if (!Plugin.Instance.Configuration.EnableAutoMerge)
            return;

        await Task.Yield();
        _movieMerger.MergeAll(progress, cancellationToken);
    }
}
