// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using BuildXL.Scheduler;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.Processes;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Engine
{
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    public abstract class IncrementalBuildTestBase : BaseEngineTest
    {
        private readonly TestCache m_testCache = new TestCache();

        protected IncrementalBuildTestBase(ITestOutputHelper output)
            : base(output)
        {
        }

        protected PathTable PathTable => Context.PathTable;

        protected void SetupTestState(Dictionary<string, bool> options = null)
        {
            var optionsBuilder = new StringBuilder();
            if (options != null)
            {
                foreach (var option in options)
                {
                    optionsBuilder.Append("export const ");
                    optionsBuilder.Append(option.Key);
                    optionsBuilder.Append(": boolean = ");
                    optionsBuilder.Append(option.Value ? "true" : "false");
                    optionsBuilder.AppendLine(";");
                }
            }

            AddModule(
                "TestModule", 
                new []
                {
                    ("builtin.dsc", "export const cmd = f`" + CmdHelper.OsShellExe + "`;"),
                    ("spec.dsc", GetSpecContents()),
                    ("options.dsc", optionsBuilder.ToString())
                }, placeInRoot: true);

            WriteInitialSources();
        }

        /// <summary>
        /// Runs an incremental build and validates that outputs are correct. Counters are not validated.
        /// </summary>
        protected BuildCounters Build(string testMarker, Action<Scheduler> inspectSchedulerData = null)
        {
            Configuration.FrontEnd.EnableIncrementalFrontEnd = false; // Turn off since the enige state is hacked and journal not populated.
            Configuration.Cache.Incremental = true;
            Configuration.Sandbox.FileAccessIgnoreCodeCoverage = true;
            IgnoreWarnings();

            // Capture scheduler
            TestHooks.Scheduler = new BoxRef<Scheduler>();
            ConfigureInMemoryCache(m_testCache);
            var snapshot = EventListener.SnapshotEventCounts();
            XAssert.AreEqual(0, EventListener.ErrorCount, "Errors already logged before attempting Build()");

            RunEngine(testMarker: testMarker);
            XAssert.AreEqual(0, EventListener.ErrorCount, "Build errors occurred, but the engine did not report failure.");
            VerifyOutputsAfterBuild(Configuration, PathTable);

            var scheduleStats = TestHooks.Scheduler.Value.SchedulerStats;
            inspectSchedulerData?.Invoke(TestHooks.Scheduler.Value);

            TestHooks.Scheduler = null;

            return new BuildCounters
            {
                PipsExecuted = EventListener.GetEventCountSinceSnapshot(EventId.ProcessPipCacheMiss, snapshot),
                ProcessPipsCached = checked((int)scheduleStats.ProcessPipsSatisfiedFromCache),
                CachedOutputsCopied = EventListener.GetEventCountSinceSnapshot(EventId.PipOutputDeployedFromCache, snapshot),
                CachedOutputsUpToDate = EventListener.GetEventCountSinceSnapshot(EventId.PipOutputUpToDate, snapshot),
                OutputsProduced = EventListener.GetEventCountSinceSnapshot(EventId.PipOutputProduced, snapshot),
                PipsBringContentToLocal = EventListener.GetEventCountSinceSnapshot(EventId.TryBringContentToLocalCache, snapshot)
            };
        }

        /// <summary>
        /// Runs an incremental build and expects it to fail. Counters are not inspected and outputs are not validated.
        /// </summary>
        protected void FailedBuild(string testMarker)
        {
            XAssert.AreEqual(0, EventListener.ErrorCount, "Errors already logged before attempting Build()");

            RunEngine(testMarker: testMarker, expectSuccess: false);

            XAssert.AreNotEqual(0, EventListener.ErrorCount, "The engine reported failure, but no build errors occurred");
        }

        /// <summary>
        /// Runs an incremental build eagerly and validates that outputs are correct.
        /// </summary>
        /// <remarks>
        /// Counters are checked to verify that everything was up-to-date. The eager behavior is forced by disabling lazy
        /// output materialization.
        /// </remarks>
        protected void EagerBuildWithoutChanges(string testMarker)
        {
            bool originalEnableLazyOutput = Configuration.Schedule.EnableLazyOutputMaterialization;
            Configuration.Schedule.EnableLazyOutputMaterialization = false;

            BuildCounters counters = Build(testMarker: testMarker);
            counters.VerifyNumberOfPipsExecuted(0);
            counters.VerifyNumberOfProcessPipsCached(TotalPips);
            VerifyNumberOfCachedOutputs(counters, totalUpToDate: TotalPips, totalCopied: 0);
            counters.VerifyNumberOfOutputsProduced(0);
            counters.VerifyNumberOfPipsBringContentToLocal(TotalPips);

            Configuration.Schedule.EnableLazyOutputMaterialization = originalEnableLazyOutput;
        }

        protected static void VerifyNumberOfCachedOutputs(BuildCounters counters, int totalUpToDate, int totalCopied)
        {
            counters.VerifyNumberOfCachedOutputsUpToDate(totalUpToDate);
            counters.VerifyNumberOfCachedOutputsCopied(totalCopied);
        }

        /// <summary>
        /// Runs an incremental build eagerly and validates that outputs are correct.
        /// </summary>
        /// <remarks>
        /// Counters are checked to verify that nothing was up-to-date. The eager behavior is forced by disabling lazy
        /// output materialization.
        /// </remarks>
        protected void EagerCleanBuild(string testMarker)
        {
            bool originalEnableLazyOutput = Configuration.Schedule.EnableLazyOutputMaterialization;
            Configuration.Schedule.EnableLazyOutputMaterialization = false;

            BuildCounters counters = Build(testMarker: testMarker);
            counters.VerifyNumberOfPipsExecuted(TotalPips);
            counters.VerifyNumberOfProcessPipsCached(0);
            counters.VerifyNumberOfCachedOutputsCopied(0);
            counters.VerifyNumberOfCachedOutputsUpToDate(0);
            counters.VerifyNumberOfOutputsProduced(TotalPipOutputs);
            counters.VerifyNumberOfPipsBringContentToLocal(0);

            Configuration.Schedule.EnableLazyOutputMaterialization = originalEnableLazyOutput;
        }
       

        /// <summary>
        /// Must be overriden to return the spec under test.
        /// </summary>
        protected abstract string GetSpecContents();

        /// <summary>
        /// Writes the initial source files.
        /// </summary>
        protected abstract void WriteInitialSources();

        /// <summary>
        /// Gets the number of pips that would be scheduled when evaluating the spec from <see cref="GetSpecContents" />
        /// </summary>
        protected abstract int TotalPips { get; }

        /// <summary>
        /// Gets the number of pip outputs that would be generated when evaluating the spec from <see cref="GetSpecContents" />
        /// </summary>
        protected abstract int TotalPipOutputs { get; }

        /// <summary>
        /// Must be overriden to validate expected outputs after building.
        /// </summary>
        protected abstract void VerifyOutputsAfterBuild(IConfiguration config, PathTable pathTable);

        protected struct BuildCounters
        {
            public int CachedOutputsCopied;
            public int CachedOutputsUpToDate;
            public int ProcessPipsCached;
            public int PipsExecuted;
            public int OutputsProduced;
            public int PipsBringContentToLocal;

            public void VerifyNumberOfPipsExecuted(int expectedPipCount)
            {
                XAssert.AreEqual(expectedPipCount, PipsExecuted, "Wrong number of pips were executed (uncached)");
            }

            public void VerifyNumberOfOutputsProduced(int expectedProducedCount)
            {
                XAssert.AreEqual(expectedProducedCount, OutputsProduced, "Wrong number of outputs were produced (uncached)");
            }

            public void VerifyNumberOfProcessPipsCached(int expectedCachedPipCount)
            {
                XAssert.AreEqual(expectedCachedPipCount, ProcessPipsCached, "Wrong number of process pips were cached (not executed)");
            }

            public void VerifyNumberOfCachedOutputsCopied(int expectedCopyCount)
            {
                XAssert.AreEqual(expectedCopyCount, CachedOutputsCopied, "Wrong number of pip outputs were copied from cache");
            }

            public void VerifyNumberOfCachedOutputsUpToDate(int expectedUpToDateCount)
            {
                XAssert.AreEqual(expectedUpToDateCount, CachedOutputsUpToDate, "Wrong number of pip outputs were already up to date (not copied)");
            }

            public void VerifyNumberOfPipsBringContentToLocal(int expectedPipsBringContentToLocal)
            {
                XAssert.AreEqual(expectedPipsBringContentToLocal, PipsBringContentToLocal, "Wrong number of pips bringing content to local");
            }
        }
    }
}
