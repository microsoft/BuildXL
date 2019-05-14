// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Utilities;
using System.IO;
using BuildXL.Pips.Builders;
using Test.BuildXL.Scheduler;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using System;
using System.Linq;
using BuildXL.Utilities.Tracing;

namespace IntegrationTest.BuildXL.Scheduler
{
    /// <summary>
    /// Tests for counters related to scheduling or pip execution.
    /// </summary>
    public class SchedulerCounterTests : SchedulerIntegrationTestBase
    {
        public SchedulerCounterTests(ITestOutputHelper output) : base(output)
        {
        }

        /// <summary>
        /// Checks that weak and strong fingerprint misses are counted
        /// </summary>
        [Fact]
        public void ValidateBasicCacheMissCounters()
        {
            AbsolutePath.TryCreate(Context.PathTable, ReadonlyRoot, out var readonlyRootPath);
            var readonlyRootDir = DirectoryArtifact.CreateWithZeroPartialSealId(readonlyRootPath);
            var childDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(ReadonlyRoot));

            // Enumerate /readonlyroot and /readonlyroot/childDir
            Process enumeratorPip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(childDir),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            Process writerPip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            ScheduleRunResult result1 = RunScheduler();
            result1.AssertCacheMiss(enumeratorPip.PipId, writerPip.PipId);
            result1.AssertNumWeakFingerprintMisses(2);

            // Create /childDir/nestedFile, causing strong fingerprint miss
            FileArtifact nestedFile = CreateSourceFile(ArtifactToString(childDir));
            ScheduleRunResult result2 = RunScheduler();
            result2.AssertCacheHit(writerPip.PipId);
            // Weak fingerprint hit on both pips
            result2.AssertNumWeakFingerprintMisses(0);
            // Strong fingerprint miss on enumeratorPip
            result2.AssertCacheMiss(enumeratorPip.PipId);
            result2.AssertNumStrongFingerprintMisses(1);
        }

        [Fact]
        public void ValidateProcessPipCountersByFilterForFailedPip()
        {
            var filterTag = "failed";
            var resetFile = CreateSourceFile();
            var resetOp = Operation.ReadFile(resetFile);

            var outputA = CreateOutputFileArtifact();
            var levleA = CreateAndSchedulePipBuilder(new Operation[]{
                resetOp,
                Operation.WriteFile(outputA),
            });
            //Pip that will fail 
            var failedPip = CreateAndSchedulePipBuilder(new Operation[]{
                resetOp,
                Operation.ReadFile(outputA),
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.Fail()
            },
            new []{ filterTag });

            var failedPipRun = RunScheduler().AssertFailure();
            AssertErrorEventLogged(EventId.PipProcessError);

            var explicitlyScheduled = failedPipRun.ProcessPipCountersByFilter.ExplicitlyScheduledProcesses;
            var implicitlyScheduled = failedPipRun.ProcessPipCountersByFilter.ImplicitlyScheduledProcesses;

            XAssert.AreEqual(0, explicitlyScheduled.GetCounterValue(PipCountersByGroup.Count));
            XAssert.AreEqual(2, implicitlyScheduled.GetCounterValue(PipCountersByGroup.Count));
            XAssert.AreEqual(2, implicitlyScheduled.GetCounterValue(PipCountersByGroup.CacheMiss));
            XAssert.AreEqual(1, implicitlyScheduled.GetCounterValue(PipCountersByGroup.Failed));


            Configuration.Filter = $"tag='{filterTag}'";
            failedPipRun = RunScheduler().AssertFailure();
            AssertErrorEventLogged(EventId.PipProcessError);

            explicitlyScheduled = failedPipRun.ProcessPipCountersByFilter.ExplicitlyScheduledProcesses;
            implicitlyScheduled = failedPipRun.ProcessPipCountersByFilter.ImplicitlyScheduledProcesses;

            XAssert.AreEqual(1, explicitlyScheduled.GetCounterValue(PipCountersByGroup.Count));
            XAssert.AreEqual(1, implicitlyScheduled.GetCounterValue(PipCountersByGroup.Count));
            XAssert.AreEqual(1, implicitlyScheduled.GetCounterValue(PipCountersByGroup.CacheHit));
            XAssert.AreEqual(1, explicitlyScheduled.GetCounterValue(PipCountersByGroup.Failed));
        }

        [Fact]
        public void ValidateProcessPipCountersByFilter()
        {
            var filterTag = "filterMatch";

            /*** Pip Set-Up ***/

            // File that all pips will depend on to easily trigger cache misses
            var resetFile = CreateSourceFile();
            var resetOp = Operation.ReadFile(resetFile);

            // Level A pips
            var outputA1 = CreateOutputFileArtifact();
            var dependencyA1 = CreateAndSchedulePipBuilder(new Operation[]
            {
                resetOp,
                Operation.WriteFile(outputA1)
            }).Process;

            // Level B (dependents of level A)
            var srcB1 = CreateSourceFile();
            var outputB1 = CreateOutputFileArtifact();
            var dependencyB1 = CreateAndSchedulePipBuilder(new Operation[]
            {
                resetOp,
                Operation.ReadFile(srcB1),
                Operation.ReadFile(outputA1),
                Operation.WriteFile(outputB1)
            }).Process;

            var outputB2 = CreateOutputFileArtifact();
            var dependencyB2 = CreateAndSchedulePipBuilder(new Operation[]
            {
                resetOp,
                Operation.ReadFile(outputA1),
                Operation.WriteFile(outputB2)
            }).Process;

            // Level C (dependents of level B)
            var outputC1 = CreateOutputFileArtifact();
            var filterMatchC1 = CreateAndSchedulePipBuilder(new Operation[]
            {
                resetOp,
                Operation.ReadFile(outputB1),
                Operation.ReadFile(outputB2),
                Operation.WriteFile(outputC1),
            },
            new[] { filterTag }).Process;

            // Level D (dependents of level C)
            var dependentD1 = CreateAndSchedulePipBuilder(new Operation[]
            {
                resetOp,
                Operation.ReadFile(outputC1),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var dependentD2 = CreateAndSchedulePipBuilder(new Operation[]
            {
                resetOp,
                Operation.ReadFile(outputC1),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var srcD3 = CreateSourceFile();
            var filterMatchD3 = CreateAndSchedulePipBuilder(new Operation[]
            {
                resetOp,
                Operation.ReadFile(srcD3),
                Operation.ReadFile(outputC1),
                Operation.WriteFile(CreateOutputFileArtifact())
            },
            new[] { filterTag });

            // Pip in its own tree
            var independentPip = CreateAndSchedulePipBuilder(new Operation[]
            {
                resetOp,
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;


            /*** 100% Cache Miss, no filter run ***/

            var cacheMissRun = RunScheduler().AssertSuccess();
            var explicitlyScheduled = cacheMissRun.ProcessPipCountersByFilter.ExplicitlyScheduledProcesses;
            var implicitlyScheduled = cacheMissRun.ProcessPipCountersByFilter.ImplicitlyScheduledProcesses;

            // If there is no filter, all pips are considered implicitly scheduled
            XAssert.AreEqual(0, explicitlyScheduled.GetCounterValue(PipCountersByGroup.Count));
            XAssert.AreEqual(8, implicitlyScheduled.GetCounterValue(PipCountersByGroup.Count));
            // Cache miss on all pips scheduled
            XAssert.AreEqual(8, implicitlyScheduled.GetCounterValue(PipCountersByGroup.CacheMiss));

            AssertProcessPipCountersByFilterSumToPipExecutorCounters(cacheMissRun);

            /*** 100% Cache hit, no filter run ***/

            var cacheHitRun = RunScheduler().AssertSuccess();
            explicitlyScheduled = cacheHitRun.ProcessPipCountersByFilter.ExplicitlyScheduledProcesses;
            implicitlyScheduled = cacheHitRun.ProcessPipCountersByFilter.ImplicitlyScheduledProcesses;

            XAssert.AreEqual(0, explicitlyScheduled.GetCounterValue(PipCountersByGroup.Count));

            if (cacheHitRun.Config.Schedule.IncrementalScheduling)
            {
                // If incremental scheduling can skip scheduling a pip, it will not be included in filter counters
                XAssert.AreEqual(0, implicitlyScheduled.GetCounterValue(PipCountersByGroup.Count));
                XAssert.AreEqual(0, implicitlyScheduled.GetCounterValue(PipCountersByGroup.CacheHit));
            }
            else
            {
                // If there is no filter, all pips are considered implicitly scheduled
                XAssert.AreEqual(8, implicitlyScheduled.GetCounterValue(PipCountersByGroup.Count));
                // Cache hit on all pips scheduled
                XAssert.AreEqual(8, implicitlyScheduled.GetCounterValue(PipCountersByGroup.CacheHit));
            }

            AssertProcessPipCountersByFilterSumToPipExecutorCounters(cacheHitRun);

            /*** 100% Cache miss, filtered run ***/

            // Add an explicit filter matching filterMatchC1, filterMatchD3
            Configuration.Filter = $"tag='{filterTag}'";

            // Cause cache miss on all pips
            ModifyFile(resetFile);

            var filterRun = RunScheduler().AssertSuccess();
            explicitlyScheduled = filterRun.ProcessPipCountersByFilter.ExplicitlyScheduledProcesses;
            implicitlyScheduled = filterRun.ProcessPipCountersByFilter.ImplicitlyScheduledProcesses;

            // filterMatchC1, filterMatchD3 are explicitly scheduled due to the filter
            XAssert.AreEqual(2, explicitlyScheduled.GetCounterValue(PipCountersByGroup.Count));
            XAssert.AreEqual(2, explicitlyScheduled.GetCounterValue(PipCountersByGroup.CacheMiss));
            // All transitive upstream dependencies are scheduled
            // dependencyA1, dependencyB1, dependencyB2
            // filterMatchC1 is not double counted as a dependency even though it is an upstream dependency of filterMatchD3
            // Matching the filter trumps being a dependency for these counters
            XAssert.AreEqual(3, implicitlyScheduled.GetCounterValue(PipCountersByGroup.Count));
            XAssert.AreEqual(3, implicitlyScheduled.GetCounterValue(PipCountersByGroup.CacheMiss));

            AssertProcessPipCountersByFilterSumToPipExecutorCounters(filterRun);

            /*** Partial cache miss for dependency, filtered run ***/

            // Cause a cache miss on dependencyB1
            ModifyFile(srcB1);

            var dependencyMissFilterRun = RunScheduler().AssertSuccess();
            explicitlyScheduled = dependencyMissFilterRun.ProcessPipCountersByFilter.ExplicitlyScheduledProcesses;
            implicitlyScheduled = dependencyMissFilterRun.ProcessPipCountersByFilter.ImplicitlyScheduledProcesses;

            // filterMatchC1, filterMatchD3 are explicitly scheduled due to the filter
            XAssert.AreEqual(2, explicitlyScheduled.GetCounterValue(PipCountersByGroup.Count));
            XAssert.AreEqual(2, explicitlyScheduled.GetCounterValue(PipCountersByGroup.CacheMiss));

            // All transitive upstream dependencies are scheduled
            // dependencyA1, dependencyB1, dependencyB2
            XAssert.AreEqual(3, implicitlyScheduled.GetCounterValue(PipCountersByGroup.Count));
            // Miss on only dependencyB1
            XAssert.AreEqual(2, implicitlyScheduled.GetCounterValue(PipCountersByGroup.CacheHit));
            XAssert.AreEqual(1, implicitlyScheduled.GetCounterValue(PipCountersByGroup.CacheMiss));

            /*** Partial cache miss for filtered pip, filtered run ***/

            // Cause a cache miss on filterMatchD3 (the downstream one)
            ModifyFile(srcD3);

            var filterMissFilterRun = RunScheduler().AssertSuccess();
            explicitlyScheduled = filterMissFilterRun.ProcessPipCountersByFilter.ExplicitlyScheduledProcesses;
            implicitlyScheduled = filterMissFilterRun.ProcessPipCountersByFilter.ImplicitlyScheduledProcesses;

            // filterMatchC1, filterMatchD3 are explicitly scheduled due to the filter
            XAssert.AreEqual(2, explicitlyScheduled.GetCounterValue(PipCountersByGroup.Count));
            // Miss on filterMatchD3
            XAssert.AreEqual(1, explicitlyScheduled.GetCounterValue(PipCountersByGroup.CacheMiss));
            // Hit on filterMatchC1 
            // Note: incremental scheduling cannot skip scheduling this pip because its output file hash is needed for filterMatchD3
            XAssert.AreEqual(1, explicitlyScheduled.GetCounterValue(PipCountersByGroup.CacheHit));

            if (filterMissFilterRun.Config.Schedule.IncrementalScheduling)
            {
                // If incremental scheduling can skip schedluing a pip, it will not be included in filter counters
                XAssert.AreEqual(0, implicitlyScheduled.GetCounterValue(PipCountersByGroup.Count));
                XAssert.AreEqual(0, implicitlyScheduled.GetCounterValue(PipCountersByGroup.CacheHit));
            }
            else
            {
                // All transitive upstream dependencies are scheduled
                // dependencyA1, dependencyB1, dependencyB2
                XAssert.AreEqual(3, implicitlyScheduled.GetCounterValue(PipCountersByGroup.Count));
                XAssert.AreEqual(3, implicitlyScheduled.GetCounterValue(PipCountersByGroup.CacheHit));
            }

            AssertProcessPipCountersByFilterSumToPipExecutorCounters(filterMissFilterRun);
        }
        
        public void ValidateProcessPipCountersByTelemetryTag()
        {
            // A <- B [blue] <- C [blue]
            // ^    ^
            // |    |
            // |    +---------- D
            // |
            // + -- E [red] <- F [red]
            // |    ^
            // |    |
            // |    +--------- G
            // |
            // +--- H [red, blue]

            const string RedTag = "red";
            const string BlueTag = "blue";
            const string TelemetryTagPrefix = "telemetry";
            Configuration.Schedule.TelemetryTagPrefix = TelemetryTagPrefix;
            string[] tags(params string[] ts) => ts.Select(t => TelemetryTagPrefix + ":" + t).ToArray();

            // Process A.
            FileArtifact aInput = CreateSourceFile();
            FileArtifact aOutput = CreateOutputFileArtifact();
            var processA = CreateAndSchedulePipBuilder(new[] { Operation.ReadFile(aInput), Operation.WriteFile(aOutput) });

            // Process B.
            FileArtifact bInput = CreateSourceFile();
            FileArtifact bOutput = CreateOutputFileArtifact();
            var processB = CreateAndSchedulePipBuilder(new[] { Operation.ReadFile(bInput), Operation.ReadFile(aOutput), Operation.WriteFile(bOutput) }, tags(BlueTag));

            // Process C.
            FileArtifact cInput = CreateSourceFile();
            FileArtifact cOutput = CreateOutputFileArtifact();
            var processC = CreateAndSchedulePipBuilder(new[] { Operation.ReadFile(cInput), Operation.ReadFile(bOutput), Operation.WriteFile(cOutput) }, tags(BlueTag));

            // Process D.
            FileArtifact dInput = CreateSourceFile();
            FileArtifact dOutput = CreateOutputFileArtifact();
            var processD = CreateAndSchedulePipBuilder(new[] { Operation.ReadFile(dInput), Operation.ReadFile(bOutput), Operation.WriteFile(dOutput) });

            // Process E.
            FileArtifact eInput = CreateSourceFile();
            FileArtifact eOutput = CreateOutputFileArtifact();
            var processE = CreateAndSchedulePipBuilder(new[] { Operation.ReadFile(eInput), Operation.ReadFile(aOutput), Operation.WriteFile(eOutput) }, tags(RedTag));

            // Process F.
            FileArtifact fInput = CreateSourceFile();
            FileArtifact fOutput = CreateOutputFileArtifact();
            var processF = CreateAndSchedulePipBuilder(new[] { Operation.ReadFile(fInput), Operation.ReadFile(eOutput), Operation.WriteFile(fOutput) }, tags(RedTag));

            // Process G.
            FileArtifact gInput = CreateSourceFile();
            FileArtifact gOutput = CreateOutputFileArtifact();
            var processG = CreateAndSchedulePipBuilder(new[] { Operation.ReadFile(gInput), Operation.ReadFile(eOutput), Operation.WriteFile(gOutput) });

            // Process H.
            FileArtifact hInput = CreateSourceFile();
            FileArtifact hOutput = CreateOutputFileArtifact();
            var processH = CreateAndSchedulePipBuilder(new[] { Operation.ReadFile(hInput), Operation.ReadFile(aOutput), Operation.WriteFile(hOutput) }, tags(RedTag, BlueTag));

            var runResult = RunScheduler().AssertSuccess();

            XAssert.AreEqual(3, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(RedTag, PipCountersByGroup.CacheMiss));
            XAssert.AreEqual(3, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(BlueTag, PipCountersByGroup.CacheMiss));

            ///////////// Modify F's input.
            ModifyFile(fInput);

            runResult = RunScheduler().AssertSuccess();

            if (Configuration.Schedule.IncrementalScheduling)
            {
                XAssert.AreEqual(1, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(RedTag, PipCountersByGroup.CacheMiss));
                XAssert.AreEqual(1, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(RedTag, PipCountersByGroup.CacheHit));
                XAssert.AreEqual(0, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(BlueTag, PipCountersByGroup.CacheMiss));
                XAssert.AreEqual(0, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(BlueTag, PipCountersByGroup.CacheHit));
            }
            else
            {
                XAssert.AreEqual(1, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(RedTag, PipCountersByGroup.CacheMiss));
                XAssert.AreEqual(2, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(RedTag, PipCountersByGroup.CacheHit));
                XAssert.AreEqual(0, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(BlueTag, PipCountersByGroup.CacheMiss));
                XAssert.AreEqual(3, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(BlueTag, PipCountersByGroup.CacheHit));
            }

            AssertOkTimeDiff(
                runResult.PipExecutorCounters.GetElapsedTime(PipExecutorCounter.ExecuteProcessDuration),
                runResult.ProcessPipCountersByTelemetryTag.GetElapsedTime(RedTag, PipCountersByGroup.ExecuteProcessDuration));

            ///////////// Modify B's input.
            ModifyFile(bInput);

            runResult = RunScheduler().AssertSuccess();

            if (Configuration.Schedule.IncrementalScheduling)
            {
                XAssert.AreEqual(0, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(RedTag, PipCountersByGroup.CacheMiss));
                XAssert.AreEqual(0, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(RedTag, PipCountersByGroup.CacheHit));
                XAssert.AreEqual(2, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(BlueTag, PipCountersByGroup.CacheMiss));
                XAssert.AreEqual(0, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(BlueTag, PipCountersByGroup.CacheHit));
            }
            else
            {
                XAssert.AreEqual(0, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(RedTag, PipCountersByGroup.CacheMiss));
                XAssert.AreEqual(3, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(RedTag, PipCountersByGroup.CacheHit));
                XAssert.AreEqual(2, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(BlueTag, PipCountersByGroup.CacheMiss));
                XAssert.AreEqual(1, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(BlueTag, PipCountersByGroup.CacheHit));
            }

            ///////////// Modify H's input.
            ModifyFile(hInput);

            runResult = RunScheduler().AssertSuccess();

            if (Configuration.Schedule.IncrementalScheduling)
            {
                XAssert.AreEqual(1, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(RedTag, PipCountersByGroup.CacheMiss));
                XAssert.AreEqual(0, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(RedTag, PipCountersByGroup.CacheHit));
                XAssert.AreEqual(1, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(BlueTag, PipCountersByGroup.CacheMiss));
                XAssert.AreEqual(0, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(BlueTag, PipCountersByGroup.CacheHit));
            }
            else
            {
                XAssert.AreEqual(1, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(RedTag, PipCountersByGroup.CacheMiss));
                XAssert.AreEqual(2, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(RedTag, PipCountersByGroup.CacheHit));
                XAssert.AreEqual(1, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(BlueTag, PipCountersByGroup.CacheMiss));
                XAssert.AreEqual(2, runResult.ProcessPipCountersByTelemetryTag.GetCounterValue(BlueTag, PipCountersByGroup.CacheHit));
            }

            AssertOkTimeDiff(
                runResult.PipExecutorCounters.GetElapsedTime(PipExecutorCounter.ExecuteProcessDuration),
                runResult.ProcessPipCountersByTelemetryTag.GetElapsedTime(RedTag, PipCountersByGroup.ExecuteProcessDuration));

            AssertOkTimeDiff(
                runResult.PipExecutorCounters.GetElapsedTime(PipExecutorCounter.ExecuteProcessDuration),
                runResult.ProcessPipCountersByTelemetryTag.GetElapsedTime(BlueTag, PipCountersByGroup.ExecuteProcessDuration));
        }

        protected void AssertProcessPipCountersByFilterSumToPipExecutorCounters(ScheduleRunResult result, PipExecutorCounter pipExecutorCounter, PipCountersByGroup pipCountersByGroup)
        {
            var explicitCounter = result.ProcessPipCountersByFilter.ExplicitlyScheduledProcesses.GetElapsedTime(pipCountersByGroup);
            var implicitCounter = result.ProcessPipCountersByFilter.ImplicitlyScheduledProcesses.GetElapsedTime(pipCountersByGroup);
            var executorCounter = result.PipExecutorCounters.GetElapsedTime(pipExecutorCounter);

            AssertOkTimeDiff(executorCounter, explicitCounter + implicitCounter, "(explicit: " + explicitCounter.TotalMilliseconds + "ms, implicit: " + implicitCounter.TotalMilliseconds + "ms, executor: " + executorCounter.TotalMilliseconds + "ms)");
        }

        /// <summary>
        /// Checks that pip counters by filters some to equivalent pip executor counters.
        /// </summary>
        protected void AssertProcessPipCountersByFilterSumToPipExecutorCounters(ScheduleRunResult result)
        {
            AssertProcessPipCountersByFilterSumToPipExecutorCounters(result, PipExecutorCounter.ProcessDuration, PipCountersByGroup.ProcessDuration);
            AssertProcessPipCountersByFilterSumToPipExecutorCounters(result, PipExecutorCounter.ExecuteProcessDuration, PipCountersByGroup.ExecuteProcessDuration);
        }

        private void AssertOkTimeDiff(TimeSpan expected, TimeSpan actual, string customMessage = default)
        {
            customMessage = customMessage ?? string.Empty;
            var timeDiff = expected - actual;
            timeDiff = timeDiff < TimeSpan.Zero ? -timeDiff : timeDiff;

            // Give a 2ms time difference buffer for rounding differences during arithmetic
            XAssert.IsTrue(timeDiff < TimeSpan.FromMilliseconds(2), "Time difference is: " + timeDiff.TotalMilliseconds + "ms, Additional message: " + customMessage);
        }

        private void ModifyFile(FileArtifact file, string content = null) => File.WriteAllText(ArtifactToString(file), content ?? Guid.NewGuid().ToString());
    }
    
    /// <summary>
    /// Extensions of <see cref="ScheduleRunResult"/> forvalidating counters.
    /// </summary>
    public static class ScheduleRunResultCounterExtensions
    {
        /// <summary>
        /// Validates that a pip execution event was counted the correct number of times
        /// </summary>
        public static void AssertPipExecutorStatCounted(this ScheduleRunResult result, PipExecutorCounter counter, long count)
            => XAssert.AreEqual(count, result.PipExecutorCounters.GetCounterValue(counter));

        /// <summary>
        /// Validates that a certain number of weak fingerprint misses were recorded
        /// </summary>
        public static void AssertNumWeakFingerprintMisses(this ScheduleRunResult result, int count) 
            => result.AssertPipExecutorStatCounted(PipExecutorCounter.CacheMissesForDescriptorsDueToWeakFingerprints, count);

        /// <summary>
        /// Validates that a certain number of strong fingerprint misses were recorded
        /// </summary>
        public static void AssertNumStrongFingerprintMisses(this ScheduleRunResult result, int count) 
            => result.AssertPipExecutorStatCounted(PipExecutorCounter.CacheMissesForDescriptorsDueToStrongFingerprints, count);
    }
}
