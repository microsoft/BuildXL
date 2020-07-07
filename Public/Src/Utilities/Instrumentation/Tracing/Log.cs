// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Tracing.CloudBuild;
using BuildXL.Utilities.Instrumentation.Common;

#pragma warning disable 1591
#pragma warning disable CA1823 // Unused field
#nullable enable

namespace BuildXL.Tracing
{
    /// <summary>
    /// Logging
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("TracingLogger")]

    public abstract partial class Logger
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log => m_log;

        /// <summary>
        /// CAUTION!!
        ///
        /// WDG has Asimov telemetry listening to this event. Any change to an existing field will require a breaking change announcement
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.Statistic,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "{statistic.Name}={statistic.Value}",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void Statistic(LoggingContext context, Statistic statistic);

        [GeneratedEvent(
            (ushort)LogEventId.FinalStatistics,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "N/A",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void FinalStatistics(LoggingContext context, IDictionary<string, long> statistics);

        [GeneratedEvent(
            (ushort)LogEventId.PipCounters,
            EventGenerators = EventGenerators.TelemetryOnly | Generators.Statistics,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "N/A",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void PipCounters(LoggingContext context, IDictionary<string, long> statistics);

        /// <summary>
        /// Logs a set of statistics. This is more efficient than logging them one by one since they go into the same
        /// telemetry event
        /// </summary>
        /// <remarks>
        /// Note that there's special handling in StatisticsEventListener to remove the event name prefix from these so
        /// they look the same as the standard Statistic events. If the name of this method is changed, the event listener
        /// must also be changed to compensate
        /// </remarks>
        [GeneratedEvent(
            (ushort)LogEventId.BulkStatistic,
            EventGenerators = Generators.Statistics,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "N/A",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void BulkStatistic(LoggingContext context, IDictionary<string, long> statistics);

        /// <summary>
        /// Logs statistics about log event occurrences and aggregate time
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.LoggerStatistics,
            EventGenerators = EventGenerators.TelemetryOnly | Generators.Statistics,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "N/A",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void LoggerStatistics(LoggingContext context, IDictionary<string, long> statistics);

        /// <summary>
        /// Logs a statistic that does not also go to the telemetry Statistic event. Use this to get data into the
        /// .stats file that is already captured in another telemetry event
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.StatisticWithoutTelemetry,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "{key}={value}",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void StatisticWithoutTelemetry(LoggingContext context, string key, long value);

        [GeneratedEvent(
            (ushort)LogEventId.Memory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Keywords.UserMessage | Keywords.Performance),
            EventTask = (ushort)Tasks.Engine,
            Message = "Managed memory: {0} MB, private bytes: {1} MB, threads: {2}")]
        public abstract void Memory(LoggingContext context, long managedHeapMegabytes, long privateMemorySizeMegabytes, int threads);

        [GeneratedEvent(
            (ushort)LogEventId.UnexpectedConditionLocal,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "{description}")]
        public abstract void UnexpectedConditionLocal(LoggingContext loggingContext, string description);

        [GeneratedEvent(
            (ushort)LogEventId.UnexpectedConditionTelemetry,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "{description}")]
        public abstract void UnexpectedConditionTelemetry(LoggingContext loggingContext, string description);

        /// <summary>
        /// Log the usage of resources and pip queues
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.Status,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "{message}",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void Status(LoggingContext context, string message);

        /// <summary>
        /// Tracer completed event
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.TracerCompletedEvent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "N/A",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void TracerCompletedEvent(LoggingContext context, string semiStableHash, string step, string procName, int tid, long timeStamp, long durationMs, string description);

        /// <summary>
        /// Tracer completed event
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.TracerCounterEvent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "N/A",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void TracerCounterEvent(LoggingContext context, string counterName, string machine, long timeStamp, long value);

        /// <summary>
        /// Tracer signal event
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.TracerSignalEvent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "N/A",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void TracerSignalEvent(LoggingContext context, string name, long timeStamp);

        /// <summary>
        /// Tracer start event
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.TracerStartEvent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "N/A",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void TracerStartEvent(LoggingContext context);

        /// <summary>
        /// Tracer stop event
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.TracerStopEvent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "N/A",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void TracerStopEvent(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.StatusCallbacksDelayed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "{ShortProductName} is experiencing a high unresponsiveness. Status events are being logged at {unresponsivenessFactor}x the expected rate",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void StatusCallbacksDelayed(LoggingContext context, int unresponsivenessFactor);

        /// <summary>
        /// Log the usage of resources and pip queues
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.StatusHeader,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "{message}",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void StatusHeader(LoggingContext context, string message);

        [GeneratedEvent(
            (ushort)LogEventId.CacheClientStats,
            EventGenerators = EventGenerators.TelemetryOnly,
            Message = "Cache Statistics for an ICache provider")]
        public abstract void ICacheStatistics(LoggingContext context, string cacheId, string cacheLevel, string cacheType, IDictionary<string, long> entryMatches);

        [GeneratedEvent(
            (ushort)LogEventId.CacheBulkStatistics,
            EventGenerators = EventGenerators.TelemetryOnly | Generators.Statistics,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "N/A",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void CacheBulkStatistics(LoggingContext context, IDictionary<string, long> statistics);

        /// <summary>
        /// Log the usage of CPU, Memory and Network resources
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.StatusSnapshot,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "N/A",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void StatusSnapshot(LoggingContext context, IDictionary<string, string> data);

        /// <summary>
        /// Logs log file event message to ETW
        /// </summary>
        [GeneratedEvent(
            (ushort)SharedLogEventId.TextLogEtwOnly,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.LogAlways,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "{logKind} {message}",
            Keywords = (int)Keywords.ExternalEtwOnly)]
        public abstract void TextLogEtwOnly(LoggingContext context, string sessionId, string logKind, int sequenceNumber, int eventNumber, string eventLabel, string message);

        #region CloudBuildEvents
        private const string CloudBuildMessageVersion = " v{cbEvent.Version}";
        private const string CloudBuildMessageVersionTargetId = " v{cbEvent.Version} Id:{cbEvent.TargetId}";

        [GeneratedEvent(
            (ushort)LogEventId.DominoInvocationEvent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Keywords.CloudBuild | Keywords.UserMessage),
            Message = "{ShortProductName}InvocationEvent" + CloudBuildMessageVersion)]
        public abstract void DominoInvocationEvent(LoggingContext context, DominoInvocationEvent cbEvent);

        [GeneratedEvent(
            (ushort)LogEventId.DominoCompletedEvent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Keywords.CloudBuild | Keywords.UserMessage),
            Message = "{ShortProductName}CompletedEvent" + CloudBuildMessageVersion)]
        public abstract void DominoCompletedEvent(LoggingContext context, DominoCompletedEvent cbEvent);

        [GeneratedEvent(
            (ushort)LogEventId.DominoContinuousStatisticsEvent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Keywords.CloudBuild | Keywords.UserMessage),
            Message = "{ShortProductName}ContinuousStatisticsEvent" + CloudBuildMessageVersion)]
        public abstract void DominoContinuousStatisticsEvent(LoggingContext context, DominoContinuousStatisticsEvent cbEvent);

        [GeneratedEvent(
            (ushort)LogEventId.TargetAddedEvent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Keywords.CloudBuild | Keywords.UserMessage),
            Message = "TargetAddedEvent" + CloudBuildMessageVersionTargetId + " Name:{cbEvent.TargetName}")]
        public abstract void TargetAddedEvent(LoggingContext context, TargetAddedEvent cbEvent);

        [GeneratedEvent(
            (ushort)LogEventId.TargetRunningEvent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Keywords.CloudBuild | Keywords.UserMessage),
            Message = "TargetRunningEvent" + CloudBuildMessageVersionTargetId)]
        public abstract void TargetRunningEvent(LoggingContext context, TargetRunningEvent cbEvent);

        [GeneratedEvent(
            (ushort)LogEventId.TargetFailedEvent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Keywords.CloudBuild | Keywords.UserMessage),
            Message = "TargetFailedEvent" + CloudBuildMessageVersionTargetId)]
        public abstract void TargetFailedEvent(LoggingContext context, TargetFailedEvent cbEvent);

        [GeneratedEvent(
            (ushort)LogEventId.TargetFinishedEvent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Keywords.CloudBuild | Keywords.UserMessage),
            Message = "TargetFinishedEvent" + CloudBuildMessageVersionTargetId)]
        public abstract void TargetFinishedEvent(LoggingContext context, TargetFinishedEvent cbEvent);

        [GeneratedEvent(
            (ushort)LogEventId.DropCreationEvent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.CloudBuild,
            Message = "'drop create': {cbEvent.ErrorMessage} {cbEvent.AdditionalInformation}")]
        public abstract void DropCreationEvent(LoggingContext context, DropCreationEvent cbEvent);

        [GeneratedEvent(
            (ushort)LogEventId.DropFinalizationEvent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.CloudBuild,
            Message = "'drop finalize': {cbEvent.ErrorMessage} {cbEvent.AdditionalInformation}")]
        public abstract void DropFinalizationEvent(LoggingContext context, DropFinalizationEvent cbEvent);
        #endregion
    }

    /// <summary>
    /// Statistic about a build
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct Statistic
    {
        /// <summary>
        /// Name of Statistic
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Value of Statistic
        /// </summary>
        public long Value { get; set; }
    }

    /// <summary>
    /// Statistics around applying filters
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct FilterStatistics
    {
        /// <summary>
        /// True if no filter is used
        /// </summary>
        public bool IsEmpty { get; set; }

        /// <summary>
        /// Count of values that could be used to short circuit evaluation
        /// </summary>
        public int ValuesToSelectivelyEvaluate { get; set; }

        /// <summary>
        /// Count of paths that could be used to short circuit evaluation
        /// </summary>
        public int PathsToSelectivelyEvaluate { get; set; }

        /// <summary>
        /// Count of modules that could be used to short circuit evaluation
        /// </summary>
        public int ModulesToSelectivelyEvaluate { get; set; }

        /// <summary>
        /// Number of output file filters
        /// </summary>
        public int OutputFileFilterCount { get; set; }

        /// <summary>
        /// Number of PipID filters
        /// </summary>
        public int PipIdFilterCount { get; set; }

        /// <summary>
        /// Number of spec file filters
        /// </summary>
        public int SpecFileFilterCount { get; set; }

        /// <summary>
        /// Number of tag filters
        /// </summary>
        public int TagFilterCount { get; set; }

        /// <summary>
        /// Number of value filters
        /// </summary>
        public int ValueFilterCount { get; set; }

        /// <summary>
        /// Number of input file filters
        /// </summary>
        public int InputFileFilterCount { get; set; }

        /// <summary>
        /// Number of negated filters
        /// </summary>
        public int NegatingFilterCount { get; set; }

        /// <summary>
        /// Number of dependent filters
        /// </summary>
        public int DependentsFilterCount { get; set; }

        /// <summary>
        /// Number of binary filters
        /// </summary>
        public int BinaryFilterCount { get; set; }

        /// <summary>
        /// Number of module filters
        /// </summary>
        public int ModuleFilterCount { get; set; }

        /// <summary>
        /// Number of multi tags filters.
        /// </summary>
        public int MultiTagsFilterCount { get; set; }
    }
}

#pragma warning restore CA1823 // Unused field
