// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Engine
{
    internal enum EngineCounter : ushort
    {
        [CounterType(CounterType.Stopwatch)]
        SchedulingQueueDisposeDuration,

        [CounterType(CounterType.Stopwatch)]
        PipGraphExporterDisposeDuration,

        [CounterType(CounterType.Stopwatch)]
        TempCleanerDisposeDuration,

        [CounterType(CounterType.Stopwatch)]
        SchedulerDisposeDuration,

        [CounterType(CounterType.Stopwatch)]
        PipTableDisposeDuration,

        [CounterType(CounterType.Stopwatch)]
        CacheDisposeDuration,

        [CounterType(CounterType.Stopwatch)]
        OrchestratorServiceDisposeDuration,

        [CounterType(CounterType.Stopwatch)]
        WorkerServiceDisposeDuration,

        [CounterType(CounterType.Stopwatch)]
        EngineScheduleDisposeDuration,

        [CounterType(CounterType.Stopwatch)]
        EngineCacheDisposeDuration,

        [CounterType(CounterType.Stopwatch)]
        EngineCacheInitDisposeDuration,

        [CounterType(CounterType.Stopwatch)]
        ChangeJournalClientDisposeDuration,

        [CounterType(CounterType.Stopwatch)]
        SnapshotCollectorPersistDuration,

        [CounterType(CounterType.Stopwatch)]
        RecordingBuildsInUserFolderDuration,

        /// <summary>
        /// Flag indicating that performance data was received from the cache
        /// </summary>
        HistoricPerfDataStoredToCache,

        /// <summary>
        /// Flag indicating that performance data was received from the cache
        /// </summary>
        HistoricPerfDataRetrievedFromCache,

        /// <summary>
        /// Flag indicating that performance data was received from the disk
        /// </summary>
        HistoricPerfDataRetrievedFromDisk,

        /// <summary>
        /// Flag indicating that performance data was sucessfully loaded
        /// </summary>
        HistoricPerfDataSuccessfullyLoaded,

        /// <summary>
        /// The loading duration for historic perf data from cache
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        HistoricPerfDataRetrievalDuration,

        /// <summary>
        /// The saving duration for historic perf data to cache
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        HistoricPerfDataSavingDuration,

        /// <summary>
        /// The duration for processing post execution tasks (saving historic perf data, historic metadatacache, exporting fingerprints, graph, etc.)
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ProcessPostExecutionTasksDuration,

        /// <summary>
        /// The number of bytes saved due to compression
        /// </summary>
        BytesSavedDueToCompression,

        /// <summary>
        /// The saving duration for fingerprint store to cache
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        FingerprintStoreSavingDuration,

        /// <summary>
        /// The number of bytes that are stored in cache for fingerprint store
        /// </summary>
        FingerprintStoreSavedSizeBytes,

        /// <summary>
        /// 1 if /exitonnewgraph flag was specified and a pip graph was scheduled to be created, 0 otherwise.
        /// </summary>
        ExitOnNewGraph
    }

    internal static class EngineCounters
    {
        /// <summary>
        /// Disposes the object and measures the duration of the disposal using the given counter
        /// </summary>
        public static void MeasuredDispose<T>(this CounterCollection<EngineCounter> counters, T disposable, EngineCounter counter)
            where T : IDisposable
        {
            using (counters.StartStopwatch(counter))
            {
                if (disposable != null)
                {
                    disposable.Dispose();
                }
            }
        }
    }
}
