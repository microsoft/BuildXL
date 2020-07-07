// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId
    {
         None = 0,
        CacheBulkStatistics = 34,
        CacheClientStats = 50,
        UnexpectedConditionLocal = 472,
        UnexpectedConditionTelemetry = 473,
        Memory = 1508,
        Statistic = 6300,
        StatisticWithoutTelemetry = 6304,
        BulkStatistic = 6305,
        FinalStatistics = 6306,
        LoggerStatistics = 6307,
        PipCounters = 6308,
       
        DominoCompletedEvent = 11500,
        TargetAddedEvent = 11501,
        TargetRunningEvent = 11502,
        TargetFailedEvent = 11503,
        TargetFinishedEvent = 11504,
        DominoInvocationEvent = 11505,
        DropCreationEvent = 11506,
        DropFinalizationEvent = 11507,
        DominoContinuousStatisticsEvent = 11508,
        Status = 12400,
        StatusSnapshot = 12401,
        StatusHeader = 12402,
        StatusCallbacksDelayed = 12403,
        TracerStartEvent = 12404,
        TracerStopEvent = 12405,
        TracerSignalEvent = 12406,
        TracerCompletedEvent = 12407,
        TracerCounterEvent = 12408,

    }
}