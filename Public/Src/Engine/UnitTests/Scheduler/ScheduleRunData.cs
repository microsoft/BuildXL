// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using BuildXL.Pips;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Fingerprints;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Encapsulates the result of a BuildXL Scheduler run for testing.
    /// </summary>
    public class ScheduleRunData
    {
        public ConcurrentDictionary<PipId, PipResultStatus> PipResults { get; } = new ConcurrentDictionary<PipId, PipResultStatus>();

        public ConcurrentDictionary<PipId, ObservedPathSet?> PathSets { get; } = new ConcurrentDictionary<PipId, ObservedPathSet?>();

        public ConcurrentDictionary<PipId, RunnableFromCacheResult> CacheLookupResults { get; } = new ConcurrentDictionary<PipId, RunnableFromCacheResult>();

        public ConcurrentDictionary<PipId, TwoPhaseCachingInfo> ExecutionCachingInfos { get; } = new ConcurrentDictionary<PipId, TwoPhaseCachingInfo>();
    }
}