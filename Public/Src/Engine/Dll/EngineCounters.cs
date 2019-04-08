// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        MasterServiceDisposeDuration,

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
        PerformanceDataStoredToCache,

        /// <summary>
        /// Flag indicating that performance data was received from the cache
        /// </summary>
        PerformanceDataRetrievedFromCache,

        /// <summary>
        /// Flag indicating that performance data was received from the disk
        /// </summary>
        PerformanceDataRetrievedFromDisk,

        /// <summary>
        /// Flag indicating that performance data was sucessfully loaded
        /// </summary>
        PerformanceDataSuccessfullyLoaded,

        /// <summary>
        /// The loading duration for running time table from cache
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        PerformanceDataRetrievalDuration,

        /// <summary>
        /// The saving duration for running time table to cache
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        PerformanceDataSavingDuration,

        /// <summary>
        /// The duration for processing post execution tasks (saving running time table, historic metadatacache, exporting fingerprints, graph, etc.)
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
        FingerprintStoreSavedSizeBytes
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
