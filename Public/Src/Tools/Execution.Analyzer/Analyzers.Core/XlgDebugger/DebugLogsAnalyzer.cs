// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Execution.Analyzer.Model;
using BuildXL.FrontEnd.Script.Debugger;
using BuildXL.Pips;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using VSCode.DebugAdapter;
using VSCode.DebugProtocol;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public const int XlgDebuggerPort = 41188;

        public Analyzer InitializeDebugLogsAnalyzer()
        {
            int port = XlgDebuggerPort;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("port", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    port = ParseInt32Option(opt, 0, 100000);
                }
                else
                {
                    throw Error("Unknown option for fingerprint text analysis: {0}", opt.Name);
                }
            }

            return new DebugLogsAnalyzer(GetAnalysisInput(), port);
        }

        private static void WriteDebugLogsAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("XLG Debugger");
        }
    }

    /// <summary>
    /// XLG debugger
    /// </summary>
    public sealed class DebugLogsAnalyzer : Analyzer
    {
        private readonly IList<PipExecutionPerformanceEventData> m_writeExecutionEntries = new List<PipExecutionPerformanceEventData>();
        private readonly Dictionary<FileArtifact, FileContentInfo> m_fileContentMap = new Dictionary<FileArtifact, FileContentInfo>(capacity: 10 * 1000);
        private readonly Dictionary<PipId, ProcessExecutionMonitoringReportedEventData> m_processMonitoringData = new Dictionary<PipId, ProcessExecutionMonitoringReportedEventData>();

        private readonly Lazy<Dictionary<PipId, PipExecutionPerformance>> m_lazyPipPerfDict;
        private readonly Lazy<Dictionary<long, PipId>> m_lazyPipsBySemiStableHash;
        private readonly Lazy<CriticalPathData> m_lazyCriticalPath;

        private string[] m_workers;
        private PathTranslator m_pathTranslator;
        private readonly CriticalPathAnalyzer m_criticalPathAnalyzer;
        private readonly int m_port;
        private readonly DebuggerState m_state;

        private XlgDebuggerState XlgState { get; }

        private IDebugger Debugger { get; set; }

        internal DebugSession Session { get; private set; }

        /// <nodoc />
        public bool IsDebugging => Debugger != null;

        /// <nodoc />
        internal DebugLogsAnalyzer(AnalysisInput input, int port)
            : base(input)
        {
            XlgState = new XlgDebuggerState(this);
            m_criticalPathAnalyzer = new CriticalPathAnalyzer(input, outputFilePath: null);
            m_lazyCriticalPath = Lazy.Create(() =>
            {
                m_criticalPathAnalyzer.Analyze();
                return m_criticalPathAnalyzer.criticalPathData;
            });
            m_port = port;
            m_state = new DebuggerState(PathTable, LoggingContext, XlgState.Render, XlgState);
            m_lazyPipPerfDict = new Lazy<Dictionary<PipId, PipExecutionPerformance>>(() =>
            {
                return m_writeExecutionEntries.ToDictionary(e => e.PipId, e => e.ExecutionPerformance);
            });
            m_lazyPipsBySemiStableHash = new Lazy<Dictionary<long, PipId>>(() =>
            {
                var result = new Dictionary<long, PipId>();
                foreach (var pipId in PipTable.Keys)
                {
                    result[PipTable.GetPipSemiStableHash(pipId)] = pipId;
                }
                return result;
            });
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            base.Dispose();
        }

        private async Task<int> AnalyzeAsync()
        {
            var debugServer = new DebugServer(LoggingContext, m_port, (d) => new DebugSession(m_state, m_pathTranslator, d));
            Debugger = await debugServer.StartAsync();
            Session = (DebugSession)Debugger.Session;
            Session.WaitSessionInitialized();

            m_state.SetThreadState(XlgState);
            Debugger.SendEvent(new StoppedEvent(XlgState.ThreadId, "Break on start", ""));

            await Session.Completion;
            return 0;
        }

        private static PathTranslator GetPathTranslator(AbsolutePath substSource, AbsolutePath substTarget, PathTable pathTable)
        {
            return substTarget.IsValid && substSource.IsValid
                ? new PathTranslator(substTarget.ToString(pathTable), substSource.ToString(pathTable))
                : null;
        }

        /// <inheritdoc />
        public override int Analyze()
        {
            return AnalyzeAsync().GetAwaiter().GetResult();
        }

        /// <nodoc />
        public IReadOnlyDictionary<PipId, PipExecutionPerformance> Pip2Perf => m_lazyPipPerfDict.Value;

        /// <nodoc />
        public IReadOnlyDictionary<long, PipId> SemiStableHash2Pip => m_lazyPipsBySemiStableHash.Value;

        /// <nodoc />
        public CriticalPathData CriticalPath => m_lazyCriticalPath.Value;

        /// <nodoc />
        public FileContentInfo? TryGetFileContentInfo(FileArtifact f)
        {
            if (m_fileContentMap.TryGetValue(f, out var result))
            {
                return result;
            }
            else
            {
                return null;
            }
        }

        /// <nodoc />
        public PipExecutionPerformance TryGetPipExePerf(PipId pipId)
        {
            return Pip2Perf.TryGetValue(pipId, out var result) ? result : null;
        }

        /// <nodoc />
        public ProcessExecutionMonitoringReportedEventData? TryGetProcessMonitoringData(PipId pipId)
        {
            if (m_processMonitoringData.TryGetValue(pipId, out var result))
            {
                return result;
            }
            else
            {
                return null;
            }
        }

        /// <nodoc />
        public PipReference AsPipReference(PipId pipId)
        {
            return new PipReference(PipTable, pipId, PipQueryContext.ViewerAnalyzer);
        }

        #region Log processing

        /// <inheritdoc />
        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            m_processMonitoringData[data.PipId] = data;
        }

        /// <inheritdoc />
        public override void DominoInvocation(DominoInvocationEventData data)
        {
            var conf = data.Configuration.Logging;
            m_pathTranslator = GetPathTranslator(conf.SubstSource, conf.SubstTarget, PathTable);
        }

        /// <inheritdoc />
        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            m_fileContentMap[data.FileArtifact] = data.FileContentInfo;
        }

        /// <inheritdoc />
        public override void WorkerList(WorkerListEventData data)
        {
            m_workers = data.Workers;
        }

        /// <inheritdoc />
        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            m_writeExecutionEntries.Add(data);
            m_criticalPathAnalyzer.PipExecutionPerformance(data);
        }

        /// <inheritdoc />
        public override void PipExecutionStepPerformanceReported(PipExecutionStepPerformanceEventData data)
        {
            m_criticalPathAnalyzer.PipExecutionStepPerformanceReported(data);
        }

        #endregion
    }
}
