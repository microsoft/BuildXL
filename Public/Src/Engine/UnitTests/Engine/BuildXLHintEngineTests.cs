// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Engine
{
    /// <summary>
    /// End-to-end tests for the <c>##bxl</c> runtime hint scanning feature (see
    /// <see cref="global::BuildXL.Pips.Operations.Process.EnableBuildXLHintScanning"/>). These drive a real pip through
    /// the engine and verify that a running time emitted by the process as <c>##bxl[runtimeSecs]=&lt;value&gt;</c> is
    /// scanned, injected into the scheduler, and surfaced downstream.
    /// </summary>
    public class BuildXLHintEngineTests : BaseEngineTest
    {
        public BuildXLHintEngineTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void InjectedRuntimeHintReachesCountersAndCriticalPath()
        {
            const long InjectedRuntimeSecs = 5;
            const long InjectedRuntimeMs = InjectedRuntimeSecs * 1000;

            var spec = GetSpec(
                GetPipExpression("result", "hintPipOut.txt", injectedRuntimeSecs: InjectedRuntimeSecs, scanForBuildXLHints: true, disableCacheLookup: false));
            AddModule("Module0", ("spec0.dsc", spec), placeInRoot: true);

            // Capture the scheduler so we can inspect its counters after the build, and enable statistics logging so
            // the critical path (where injected times are surfaced) is emitted.
            Configuration.Engine.LogStatistics = true;
            TestHooks.Scheduler = new BoxRef<Scheduler>();
            RunEngine();

            var scheduler = TestHooks.Scheduler.Value;

            // 1) The injected running time reached the dedicated telemetry counter that tracks time injected via hints.
            //    This is a stopwatch counter, so the value round-trips through integer stopwatch ticks and can come back
            //    a fraction of a millisecond short of the exact injected value (but never above it). A lower-bound check
            //    is therefore both sufficient and robust: a near-zero counter would mean the injection never happened.
            var injectedDuration = scheduler.PipExecutionCounters.GetElapsedTime(PipExecutorCounter.InjectedProcessDuration);
            XAssert.IsTrue(
                injectedDuration.TotalMilliseconds >= InjectedRuntimeMs - 1,
                $"Expected the InjectedProcessDuration counter to be at least ~{InjectedRuntimeMs}ms but it was {injectedDuration.TotalMilliseconds}ms.");

            // 2) The injected running time was surfaced as '[injected]' on the critical path output. Because the injected
            //    time dominates every other pip's runtime, our pip is guaranteed to be on the critical path.
            var criticalPathMessages = EventListener.GetLogMessagesForEventId((int)global::BuildXL.Scheduler.Tracing.LogEventId.CriticalPathChain).ToArray();
            XAssert.IsTrue(
                criticalPathMessages.Any(m => m.Contains("[injected]")),
                "Expected the critical path output to mark the injected running time with '[injected]'. Critical path messages: "
                + string.Join(Environment.NewLine, criticalPathMessages));
        }

        /// <summary>
        /// Two-build scenario proving the scheduler <em>consumes</em> an injected runtime for scheduling on a
        /// subsequent build. Build 1 runs three independent, uncacheable pips that can run in parallel: one emits a
        /// large <c>##bxl[runtimeSecs]</c> hint (injected and persisted into the historic perf data table), the other two
        /// are near-instant plain pips. Build 2 re-runs all three pips; the scheduler loads the persisted historic data
        /// and computes pip priorities from it. We assert that (a) the two non-injected pips end up with essentially the
        /// same scheduling priority and (b) the pip with the injected (large) historic runtime is assigned a much higher
        /// priority than both - i.e. the injected runtime actually influences scheduling decisions.
        /// </summary>
        [Fact]
        public void InjectedRuntimeIsConsumedForSchedulingOnSubsequentBuild()
        {
            // One hour: dominates the plain pips' near-zero runtime, and stays well below the critical-path priority
            // saturation limit so the priority difference is unambiguous.
            const long InjectedRuntimeSecs = 3600;
            const long InjectedRuntimeMs = InjectedRuntimeSecs * 1000;

            // The two non-injected pips carry no injected runtime; their priorities are derived from their own
            // (near-zero) independently measured runtimes, so they should be essentially identical - differing at most
            // by a few ms of execution-time noise, which maps directly into the low bits of the priority. We allow a
            // small tolerance rather than asserting exact equality, and require the injected pip to exceed both by far
            // more than that tolerance.
            const int PriorityNoiseTolerance = 1000;

            var spec = GetSpec(
                GetPipExpression("injected", "injectedOut.txt", injectedRuntimeSecs: InjectedRuntimeSecs, scanForBuildXLHints: true, disableCacheLookup: true),
                GetPipExpression("plain1", "plain1Out.txt", injectedRuntimeSecs: null, scanForBuildXLHints: false, disableCacheLookup: true),
                GetPipExpression("plain2", "plain2Out.txt", injectedRuntimeSecs: null, scanForBuildXLHints: false, disableCacheLookup: true));
            AddModule("Module0", ("spec0.dsc", spec), placeInRoot: true);

            // The historic perf data table is only persisted when a build ran longer than a threshold (default 3
            // minutes). Our test builds finish in seconds, so override that threshold to zero via the engine
            // configuration so the table is persisted between the two builds.
            Configuration.Engine.PostExecOptimizeThreshold = TimeSpan.Zero;

            // Build 1: execute all pips. The injected pip's hint is scanned, injected, and the resulting historic
            // runtime is persisted to disk.
            RunEngine(testMarker: "Build 1 - inject and persist historic runtime");

            // Build 2: all pips re-run (they are uncacheable). Capture the scheduler so we can inspect the pip
            // priorities it computed from the historic data persisted by build 1.
            TestHooks.Scheduler = new BoxRef<Scheduler>();
            RunEngine(testMarker: "Build 2 - consume persisted historic runtime");

            var scheduler = TestHooks.Scheduler.Value;

            // Sanity check that the historic perf data persisted by build 1 was actually reloaded from disk by
            // build 2 (rather than build 2 silently starting from an empty table). Build 1 has nothing to load, so
            // this event is emitted only by build 2 and must report the three entries (injected + two plain pips).
            var historicLoadedMessages = EventListener
                .GetLogMessagesForEventId((int)global::BuildXL.Engine.Tracing.LogEventId.HistoricPerfDataLoaded)
                .ToArray();
            XAssert.IsTrue(
                historicLoadedMessages.Any(m => m.Contains("3 entries")),
                "Expected build 2 to load the three historic perf data entries persisted by build 1. Historic-perf-loaded messages: "
                + string.Join(Environment.NewLine, historicLoadedMessages));

            var processPips = scheduler.PipGraph.RetrievePipsOfType(PipType.Process).Cast<Process>().ToList();
            XAssert.AreEqual(3, processPips.Count, "Expected exactly three process pips in the graph.");

            var injectedPip = processPips.Single(p => p.EnableBuildXLHintScanning);
            var plainPips = processPips.Where(p => !p.EnableBuildXLHintScanning).ToList();
            XAssert.AreEqual(2, plainPips.Count, "Expected exactly two non-injected process pips.");

            int injectedPriority = scheduler.GetPipPriority(injectedPip.PipId);
            int plainPriority0 = scheduler.GetPipPriority(plainPips[0].PipId);
            int plainPriority1 = scheduler.GetPipPriority(plainPips[1].PipId);

            // (a) The two non-injected pips should have essentially the same scheduling priority.
            XAssert.IsTrue(
                Math.Abs(plainPriority0 - plainPriority1) <= PriorityNoiseTolerance,
                $"The two non-injected pips should have essentially the same scheduling priority. Priorities: {plainPriority0} and {plainPriority1}.");

            // (b) The injected pip's large historic runtime must push its priority far above both non-injected pips.
            XAssert.IsTrue(
                injectedPriority > plainPriority0 + PriorityNoiseTolerance && injectedPriority > plainPriority1 + PriorityNoiseTolerance,
                $"On the second build the pip with the injected historic runtime ({InjectedRuntimeMs}ms) should have a much higher scheduling "
                + $"priority than the non-injected pips. Injected: {injectedPriority}, non-injected: {plainPriority0} and {plainPriority1}.");
        }

        /// <summary>
        /// Wraps one or more pip expressions (see <see cref="GetPipExpression"/>) into a complete, self-contained
        /// DScript spec: the transformer import, the object-root mount, and the shared <c>execute</c> helper.
        /// </summary>
        private string GetSpec(params string[] pipExpressions)
        {
            return $@"
import {{Transformer}} from 'Sdk.Transformers';

const outDir = Context.getNewOutputDirectory('hint');

{GetExecuteFunction()}

{string.Join(Environment.NewLine + Environment.NewLine, pipExpressions)}
";
        }

        /// <summary>
        /// Produces a single <c>execute({...})</c> pip expression. The pip runs the OS shell to write a declared output
        /// file. When <paramref name="injectedRuntimeSecs"/> is provided the pip additionally emits a
        /// <c>##bxl[runtimeSecs]=value</c> hint on its captured standard output (which BuildXL scans).
        /// </summary>
        /// <param name="variableName">The DScript const name bound to the pip's result (must be unique within a spec).</param>
        /// <param name="outputFileName">The name of the pip's declared output file, created under the object root.</param>
        /// <param name="injectedRuntimeSecs">When set, the runtime (in whole seconds) to emit as a <c>##bxl[runtimeSecs]</c> hint; when null, no hint is emitted.</param>
        /// <param name="scanForBuildXLHints">Whether to enable <c>##bxl</c> hint scanning on the pip.</param>
        /// <param name="disableCacheLookup">Whether to mark the pip uncacheable so it always re-executes.</param>
        private string GetPipExpression(string variableName, string outputFileName, long? injectedRuntimeSecs, bool scanForBuildXLHints, bool disableCacheLookup)
        {
            var shellCommand = GetShellCommand(injectedRuntimeSecs);

            var options = new List<string>();
            if (scanForBuildXLHints)
            {
                options.Add("scanForBuildXLHints: true,");
            }

            if (disableCacheLookup)
            {
                options.Add("disableCacheLookup: true,");
            }

            var optionsBlock = string.Join(Environment.NewLine + "    ", options);

            return $@"const {variableName} = execute({{
    tool: {GetOsShellCmdToolDefinition()},
    workingDirectory: d`.`,
    arguments: [
        {{ value: {{ value: '{shellCommand}', kind: ArgumentKind.rawText }}}},
        {{ value: {{ value: ' {(OperatingSystemHelper.IsLinuxOS ? "| tee -a" : ">")} ', kind: ArgumentKind.rawText }}}},
        {{ value: {{ path: p`${{outDir}}/{outputFileName}`, kind: ArtifactKind.output }}}}{ClosingQuoteIfNeeded()}
        ],
    outputs: [],
    {optionsBlock}
}});";
        }

        /// <summary>
        /// Builds the OS-shell argument string that emits the hint on the process' captured standard output. On Windows the
        /// hint echo is followed by a trailing <c>echo hi</c>; the caller appends <c>&gt;</c> so only that trailing echo is
        /// redirected into the declared output file, leaving the hint on the console. On Unix the caller appends
        /// <c>| tee -a</c>, which writes the (single) hint echo to both the console and the declared output file.
        /// </summary>
        private static string GetShellCommand(long? injectedRuntimeSecs)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                var hint = injectedRuntimeSecs.HasValue ? $"echo \\\\#\\\\#bxl[runtimeSecs]={injectedRuntimeSecs.Value} " : "echo hi ";
                return $"-c \"{hint}";
            }
            else
            {
                // The hint echo goes to the console (scanned for the '##bxl' hint); the trailing 'echo hi', which the caller
                // redirects with '>', writes the declared output file. cmd binds '>' to the last command only, so the hint
                // stays on the console. (cmd has no 'tee', so unlike Unix we cannot write the hint itself to both.)
                var hint = injectedRuntimeSecs.HasValue ? $"echo ##bxl[runtimeSecs]={injectedRuntimeSecs.Value} & " : string.Empty;
                return $"/D /C {hint}echo hi ";
            }
        }

        /// <summary>
        /// On Unix the shell command opens a quote that must be closed with an extra raw-text argument; on Windows no
        /// closing quote is needed.
        /// </summary>
        private static string ClosingQuoteIfNeeded()
        {
            return OperatingSystemHelper.IsUnixOS
                ? @", {value: {value: '""', kind: ArgumentKind.rawText}}"
                : string.Empty;
        }
    }
}
