// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Engine;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using BuildXL.Execution.Analyzer;
using Xunit.Abstractions;
using BuildXL.Utilities.Collections;
using System.Collections.Generic;
using BuildXL.Utilities;
using System.IO;
using static BuildXL.ToolSupport.CommandLineUtilities;
using System.Text;
using System;

namespace Test.Tool.Analyzers
{
    /// <summary>
    /// Methods for testing execution analyzers that read the BuildXL execution log
    /// </summary>
    public class AnalyzerTestBase : SchedulerIntegrationTestBase
    {
        /// <summary>
        /// The analysis mode of the analyzer. 
        /// </summary>
        /// <remarks>
        /// Because this is a required parameter of all execution analyzers, 
        /// it must be explicitly set by child test classes before calling
        /// <see cref="RunAnalyzer(ScheduleRunResult, ScheduleRunResult, IEnumerable{Option})"/>.
        /// </remarks>
        protected AnalysisMode AnalysisMode { get; set; }

        /// <summary>
        /// The default value of required command line arguments for analysis modes that add additional required arguments.
        /// </summary>
        /// <remarks>
        /// Because additional arguments differ between analyzers,
        /// this must be explicitly set to a non-null value by child test classes before calling
        /// <see cref="RunAnalyzer(ScheduleRunResult, ScheduleRunResult, IEnumerable{Option})"/>.
        /// An empty array is valid.
        /// </remarks>
        protected IEnumerable<Option> ModeSpecificDefaultArgs = null;

        /// <summary>
        /// Read at the end of <see cref="RunAnalyzer(ScheduleRunResult, ScheduleRunResult, IEnumerable{Option})"/>
        /// and stored in <see cref="AnalyzerResult.FileOutput"/>.
        /// This is intended for analyzers that produce output files instead of writing to standard out.
        /// </summary>
        protected string ResultFileToRead = null;

        public AnalyzerTestBase(ITestOutputHelper output) : base(output)
        {
            Configuration.Logging.LogsToRetain = int.MaxValue;
            Configuration.Logging.LogExecution = true;
            AnalysisMode = AnalysisMode.None;
        }

        /// <summary>
        /// Serializes and saves state needed for execution analyzers.
        /// </summary>
        private Task<bool> SaveExecutionStateToDiskAsync(ScheduleRunResult result)
        {
            // Every scheduler run has a unique log directory based off timestamp of run
            string logDirectory = Path.Combine(
                result.Config.Logging.LogsDirectory.ToString(Context.PathTable),
                result.Config.Logging.LogPrefix);

            var serializer = new EngineSerializer(
                LoggingContext,
                logDirectory,
                correlationId: FileEnvelopeId.Create());

            var dummyHistoricData = (IReadOnlyList<HistoricDataPoint>) CollectionUtilities.EmptyArray<HistoricDataPoint>();
            var dummyHistoricTableSizes = new HistoricTableSizes(dummyHistoricData);

            return EngineSchedule.SaveExecutionStateToDiskAsync(
                serializer,
                Context,
                PipTable,
                result.Graph,
                Expander,
                dummyHistoricTableSizes);
        }

        /// <summary>
        /// Saves the result of the BuildXL execution to disk and creates
        /// a <see cref="Option"/> for execution analyzers representing the 
        /// execution result's location on disk.
        /// </summary>
        private Option PrepareAndCreateExecutionLogArg(ScheduleRunResult build)
        {
            // Save the execution state to disk to use as inputs into the analyzer
            SaveExecutionStateToDiskAsync(build).GetAwaiter().GetResult();

            // Build the command line arguments for the analyzer
            return new Option
            {
                Name = "xl",
                Value = build.Config.Logging.LogsDirectory.ToString(Context.PathTable),
            };
        }

        /// <summary>
        /// Creates the string command line arguments for the analyzer
        /// </summary>
        private string[] BuildCommandLineArgs(ScheduleRunResult buildA, ScheduleRunResult buildB = null, IEnumerable<Option> additionalArgs = null)
        {
            var stringArgs = new List<string>
            {
                // Add required mode argument
                new Option
                {
                    Name = "m",
                    Value = AnalysisMode.ToString()
                }.PrintCommandLineString(),

                // Disable telemetry for tests
                new Option
                {
                    Name = "disableTelemetry"
                }.PrintCommandLineString(),

                // Add required execution log argument
                PrepareAndCreateExecutionLogArg(buildA).PrintCommandLineString()
            };

            // Add optional execution log argument
            if (buildB != null)
            {
                stringArgs.Add(PrepareAndCreateExecutionLogArg(buildB).PrintCommandLineString());
            }

            // Add required args for specific analyzer
            foreach (var arg in ModeSpecificDefaultArgs)
            {
                stringArgs.Add(arg.PrintCommandLineString());
            }

            if (additionalArgs != null)
            {
                // Add any additional args for this specific run
                foreach (var arg in additionalArgs)
                {
                    stringArgs.Add(arg.PrintCommandLineString());
                }
            }

            return stringArgs.ToArray();
        }

        /// <summary>
        /// Runs an execution analyzer in <see cref="AnalysisMode"/>.
        /// </summary>
        /// <param name="buildA">
        /// The scheduler run to analyze
        /// </param>
        /// <param name="buildB">
        /// Optional second scheduler run to analyze for modes that
        /// compare execution logs
        /// </param>
        /// <param name="additionalArgs">
        /// Additional options applicable to only this particular analyzer run
        /// </param>
        /// <returns>
        /// string path to results file
        /// </returns>
        public AnalyzerResult RunAnalyzer(ScheduleRunResult buildA, ScheduleRunResult buildB = null, IEnumerable<Option> additionalArgs = null)
        {
            // The test class must set an analysis mode in the constructor
            XAssert.IsTrue(AnalysisMode != AnalysisMode.None);
            // The test class must set the default command line args in the constructor
            XAssert.IsTrue(ModeSpecificDefaultArgs != null);

            string[] commandLineArgs = BuildCommandLineArgs(buildA, buildB: buildB, additionalArgs: additionalArgs);

            // Run the analyzer with console output redirected to analyzer result
            var result = new AnalyzerResult();
            using (var console = new ConsoleRedirector(ref result.StandardOutput))
            {
                result.ExitCode = Program.Main(commandLineArgs);
            }

            if (ResultFileToRead != null)
            {
                XAssert.IsTrue(File.Exists(ResultFileToRead));
                result.FileOutput = File.ReadAllText(ResultFileToRead);
            }

            return result;
        }

        /// <summary>
        /// Since these tests are meant to test analyzer tools and not BuildXL itself, 
        /// cut out validating BuildXL errors and warnings on test dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    // ValidateWarningsAndErrors();

                    if (m_ioCompletionTraceHook != null)
                    {
                        m_ioCompletionTraceHook.AssertTracedIOCompleted();
                    }
                }
                finally
                {
                    if (m_eventListener != null)
                    {
                        m_eventListener.Dispose();
                        m_eventListener = null;
                    }

                    if (m_ioCompletionTraceHook != null)
                    {
                        m_ioCompletionTraceHook.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Temporarily redirects console output to strings while the 
        /// object is in scope. Console output returns to previous location on dispose.
        /// </summary>
        public class ConsoleRedirector : IDisposable
        {
            private TextWriter m_originalConsoleOut;
            private StringBuilder m_standardOutput = new StringBuilder();
            private string m_outStringTarget;

            /// <summary>
            /// Start redirecting console output.
            /// </summary>
            public ConsoleRedirector(ref string outString)
            {
                m_originalConsoleOut = Console.Out;
                m_outStringTarget = outString;
                Console.SetOut(new StringWriter(m_standardOutput));
            }

            /// <summary>
            /// Return console output to previous locations.
            /// </summary>
            public void Dispose()
            {
                m_outStringTarget = m_standardOutput.ToString();
                Console.SetOut(m_originalConsoleOut);
            }
        }

        public class AnalyzerResult
        {
            /// <summary>
            /// Exit code of analyzer.
            /// </summary>
            public int ExitCode = -1;

            /// <summary>
            /// The standard output, if any, produced by the analyzer.
            /// </summary>
            public string StandardOutput;

            /// <summary>
            /// The contents of the output file, if any, produced by the analyzer.
            /// </summary>
            public string FileOutput;

            public AnalyzerResult AssertSuccess()
            {
                XAssert.IsTrue(ExitCode == 0);
                return this;
            }
        }
    }
}
