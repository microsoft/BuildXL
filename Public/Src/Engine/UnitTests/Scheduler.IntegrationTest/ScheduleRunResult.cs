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
    public class ScheduleRunResult
    {
        public PipGraph Graph { get; set; }

        public IConfiguration Config { get; set; }

        public bool Success { get; set; }

        public ConcurrentDictionary<PipId, PipResultStatus> PipResults { get; set; }

        public ConcurrentDictionary<PipId, ObservedPathSet?> PathSets { get; set; }

        public CounterCollection<PipExecutorCounter> PipExecutorCounters { get; set; }

        public PipCountersByFilter ProcessPipCountersByFilter { get; set; }

        public PipCountersByTelemetryTag ProcessPipCountersByTelemetryTag { get; set; }

        public SchedulerState SchedulerState { get; set; }

        public ScheduleRunResult AssertSuccess()
        {
            XAssert.IsTrue(Success, "Expected scheduler to run with all pips being successful");
            return this;
        }

        public ScheduleRunResult AssertFailure()
        {
            XAssert.IsFalse(Success, "Expected scheduler to run with at least one pip failing");
            return this;
        }

        /// <summary>
        /// Validates that if the scheduler returns success, no errors are logged. If the scheduler returns a failure, errors mubt be logged.
        /// </summary>
        /// <param name="loggingContext"></param>
        public void AssertSuccessMatchesLogging(LoggingContext loggingContext)
        {
            // Validate that error logging is correct
            if (Success)
            {
                if (loggingContext.ErrorWasLogged)
                {
                    XAssert.Fail("No error should be logged if status is success. " +
                        "Errors logged: " + string.Join(", ", loggingContext.ErrorsLoggedById.ToArray()));
                }
            }
            else
            {
                XAssert.IsTrue(loggingContext.ErrorWasLogged, "Error should have been logged for non-success");
            }
        }

        /// <summary>
        /// Validates that a pip was a cache hit. This method works even if incremental scheduling filtered
        /// out the pip
        /// </summary>
        public ScheduleRunResult AssertCacheHit(params PipId[] pipIds) => AssertSuccess().AssertCacheHitWithoutAssertingSuccess(pipIds);

        /// <summary>
        /// Validates that a pip was a cache hit without asserting all pips being successful.
        /// This method works even if incremental scheduling filtered out the pip.
        /// </summary>
        public ScheduleRunResult AssertCacheHitWithoutAssertingSuccess(params PipId[] pipIds)
        {
            PipResultStatus status;
            for (int i = 0; i < pipIds.Length; i++)
            {
                PipId pipId = pipIds[i];
                if (PipResults.TryGetValue(pipId, out status))
                {
                    XAssert.AreNotEqual(PipResultStatus.Succeeded, status, "A pip ran, but it should have been a cache hit. Pip at 0-based parameter index: " + i);
                    XAssert.AreNotEqual(PipResultStatus.Failed, status, "A pip ran, but it should have been a cache hit. Pip at 0-based parameter index: " + i);
                }
            }

            return this;
        }

        /// <summary>
        /// Validates that a pip was a cache miss.
        /// </summary>
        public ScheduleRunResult AssertCacheMiss(params PipId[] pipIds) => AssertSuccess().AssertCacheMissWithoutAssertingSuccess(pipIds);

        /// <summary>
        /// Validates that a pip was a cache miss without asserting all pips being successful.
        /// </summary>
        public ScheduleRunResult AssertCacheMissWithoutAssertingSuccess(params PipId[] pipIds)
        {
            PipResultStatus status;
            for (int i = 0; i < pipIds.Length; i++)
            {
                PipId pipId = pipIds[i];
                if (PipResults.TryGetValue(pipId, out status))
                {
                    XAssert.AreEqual(PipResultStatus.Succeeded, status, "A pip was a cache hit, but it should have been a cache miss. Pip at 0-based parameter index: " + i);
                }
                else
                {
                    XAssert.Fail("A pip did not run, but it should have been a cache miss. Pip at 0-based parameter index: " + i);
                }
            }

            // Check that our counters for cache misses are working
            ValidateCacheMissTypesSumToTotal();

            return this;
        }

        /// <summary>
        /// Validates that a pip contains the given observed path entries
        /// </summary>
        public ScheduleRunResult AssertObservation(PipId pipId, params ObservedPathEntry[] observedPaths)
        {
            AssertSuccess();
            foreach (var observedPath in observedPaths)
            {
                if (!PathSets[pipId].HasValue || !PathSets[pipId].Value.Paths.Contains(observedPath))
                {
                    XAssert.Fail("The pip does not have the given observed path entry");
                }
            }

            return this;
        }

        /// <summary>
        /// Verifies pips and their expected <see cref="PipResultStatus"/>.
        /// </summary>
        public ScheduleRunResult AssertPipResultStatus(params (PipId pipId, PipResultStatus status)[] pipAndExpectedStatus)
        {
            for (int i = 0; i < pipAndExpectedStatus.Length; ++i)
            {
                if (PipResults.TryGetValue(pipAndExpectedStatus[i].pipId, out var actualStatus))
                {
                    XAssert.AreEqual(pipAndExpectedStatus[i].status, actualStatus, "Pip at index " + i + " has an unexpected status");
                }
                else
                {
                    XAssert.Fail("Pip at index " + i + " is not scheduled");
                }
            }

            return this;
        }

        /// <summary>
        /// Check the sum of cache miss reasons equals the total cache misses
        /// </summary>
        private void ValidateCacheMissTypesSumToTotal()
        {
            // All mutually exclusive counters for cache miss reasons

            IEnumerable<PipExecutorCounter> cacheMissTypes = PipExecutor.GetListOfCacheMissTypes();

            long sum = 0;
            foreach (var missType in cacheMissTypes)
            {
                sum += PipExecutorCounters.GetCounterValue(missType);
            }

            XAssert.AreEqual(PipExecutorCounters.GetCounterValue(PipExecutorCounter.ProcessPipsExecutedDueToCacheMiss), sum);
        }
    }
}
