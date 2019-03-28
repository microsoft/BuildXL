// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Scheduler;
using BuildXL.Scheduler.IncrementalScheduling;
using BuildXL.Storage.ChangeTracking;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeIncrementalSchedulingStateAnalyzer()
        {
            string outputFile = null;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFile = ParseSingletonPathOption(opt, outputFile);
                }
                else if (opt.Name.Equals("graphAgnostic", StringComparison.OrdinalIgnoreCase) ||
                         opt.Name.Equals("g", StringComparison.OrdinalIgnoreCase))
                {
                    // Do nothing for compatibility with Office BuildXL Analyzer.
                }
                else
                {
                    throw Error("Unknown option for incremental scheduling analysis: {0}", opt.Name);
                }
            }

            return new IncrementalSchedulingStateAnalyzer(GetAnalysisInput(), outputFile);
        }

        private static void WriteIncrementalSchedulingStateAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Incremental Scheduling State Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.IncrementalSchedulingState), "Generates textual format of incremental scheduling state");
            writer.WriteOption("outputFile", "Required. The output file.", shortName: "o");
        }
    }

    internal class IncrementalSchedulingStateAnalyzer : Analyzer
    {
        private readonly string m_cachedGraphDirectory;
        private readonly string m_outputFile;

        public IncrementalSchedulingStateAnalyzer(AnalysisInput input, string outputFile)
            : base(input)
        {
            m_cachedGraphDirectory = input.CachedGraphDirectory;
            m_outputFile = outputFile;
        }

        public override int Analyze()
        {
            Console.WriteLine("Loading incremental scheduling state from cache graph directory '{0}'", m_cachedGraphDirectory);

            var loggingContext = new LoggingContext(nameof(IncrementalSchedulingStateAnalyzer));
            var trackerFile = Path.Combine(m_cachedGraphDirectory, Scheduler.Scheduler.DefaultSchedulerFileChangeTrackerFile);
            FileChangeTracker fileChangeTracker;
            var loadResult = FileChangeTracker.LoadTrackingChanges(
                loggingContext,
                null,
                null,
                trackerFile,
                null, 
                out fileChangeTracker,
                loadForAllCapableVolumes: false);

            if (!loadResult.Succeeded)
            {
                Console.Error.WriteLine("Unable to load file change tracker '" + trackerFile + "'");
                return 1;
            }

            var incrementalSchedulingStateFile = Path.Combine(m_cachedGraphDirectory, Scheduler.Scheduler.DefaultIncrementalSchedulingStateFile);
            var factory = new IncrementalSchedulingStateFactory(loggingContext, analysisMode: true);
            
            var incrementalSchedulingState = factory.LoadOrReuse(
                fileChangeTracker.FileEnvelopeId,
                CachedGraph.PipGraph,
                null,
                WellKnownContentHashes.AbsentFile,
                incrementalSchedulingStateFile,
                schedulerState: null);

            if (incrementalSchedulingState == null)
            {
                Console.Error.WriteLine("Unable to load incremental scheduling state '" + incrementalSchedulingStateFile + "'");
                return 1;
            }

            using (var writer = File.CreateText(Path.GetFullPath(m_outputFile)))
            {
                incrementalSchedulingState.WriteText(writer);
                writer.WriteLine(string.Empty);
                fileChangeTracker.WriteText(writer);
            }

            return 0;
        }
    }
}
