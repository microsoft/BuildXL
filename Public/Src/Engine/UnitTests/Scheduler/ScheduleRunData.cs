// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities.Configuration;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Test.BuildXL.TestUtilities.Xunit;
using BuildXL.Utilities.Tracing;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities.Instrumentation.Common;

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