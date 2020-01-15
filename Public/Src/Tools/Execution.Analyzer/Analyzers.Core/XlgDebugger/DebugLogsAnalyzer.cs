// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
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
            bool enableCaching = false;
            bool ensureOrdering = true;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("port", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    port = ParseInt32Option(opt, 0, 100000);
                }
                else if (
                    opt.Name.Equals("evalCache-", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("evalCache+", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("evalCache", StringComparison.OrdinalIgnoreCase))
                {
                    enableCaching = ParseBooleanOption(opt);
                }
                else if (
                    opt.Name.Equals("ordered-", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("ordered+", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("ordered", StringComparison.OrdinalIgnoreCase))
                {
                    ensureOrdering = ParseBooleanOption(opt);
                }
                else
                {
                    throw Error("Unknown option for fingerprint text analysis: {0}", opt.Name);
                }
            }

            return new DebugLogsAnalyzer(GetAnalysisInput(), port, enableCaching, ensureOrdering);
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
        private readonly Dictionary<PipId, Dictionary<FingerprintComputationKind, ProcessFingerprintComputationEventData>> m_processFingerprintData = new Dictionary<PipId, Dictionary<FingerprintComputationKind, ProcessFingerprintComputationEventData>>();
        private readonly Dictionary<DirectoryArtifact, ReadOnlyArray<FileArtifact>> m_sharedOpaqueOutputs = new Dictionary<DirectoryArtifact, ReadOnlyArray<FileArtifact>>();

        private readonly Lazy<Dictionary<PipId, PipExecutionPerformance>> m_lazyPipPerfDict;
        private readonly Lazy<Dictionary<long, PipId>> m_lazyPipsBySemiStableHash;
        private readonly Lazy<CriticalPathData> m_lazyCriticalPath;
        private readonly MultiValueDictionary<AbsolutePath, DirectoryMembershipHashedEventData> m_dirData;

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
        public bool EnableEvalCaching { get; }

        /// <nodoc />
        public bool EnsureOrdering { get; }

        /// <nodoc />
        internal DebugLogsAnalyzer(AnalysisInput input, int port, bool enableCaching, bool ensureOrdering, bool preHydrateProcessPips = true)
            : base(input)
        {
            m_port = port;
            EnableEvalCaching = enableCaching;
            EnsureOrdering = ensureOrdering;
            XlgState = new XlgDebuggerState(this);
            m_dirData = new MultiValueDictionary<AbsolutePath, DirectoryMembershipHashedEventData>();
            m_criticalPathAnalyzer = new CriticalPathAnalyzer(input, outputFilePath: null);
            m_lazyCriticalPath = Lazy.Create(() =>
            {
                m_criticalPathAnalyzer.Analyze();
                return m_criticalPathAnalyzer.criticalPathData;
            });
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
            
            if (preHydrateProcessPips)
            {
                Task
                    .Run(() =>
                    {
                        var start = DateTime.UtcNow;
                        Console.WriteLine("=== Started hydrating process pips");
                        Analysis.IgnoreResult(PipGraph.RetrievePipsOfType(Pips.Operations.PipType.Process).ToArray());
                        Console.WriteLine("=== Done hydrating process pips in " + DateTime.UtcNow.Subtract(start));
                    })
                    .Forget(ex => 
                    {
                        Console.WriteLine("=== Prehydrating pips failed: " + ex);
                    });
            }

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
            return m_processMonitoringData.TryGetValue(pipId, out var result) ? result : default;
        }

        /// <nodoc />
        public object TryGetProcessFingerprintData(PipId pipId)
        {
            return m_processFingerprintData.TryGetValue(pipId, out var result) ? result.ToArray() : null;
        }

        /// <nodoc />
        public IEnumerable<DirectoryMembershipHashedEventData> GetDirMembershipData()
        {
            return m_dirData.Values.SelectMany(d => d);
        }

        /// <nodoc />
        public IEnumerable<FileArtifact> GetDirMembers(DirectoryArtifact dir)
        {
            return m_sharedOpaqueOutputs.TryGetValue(dir, out var members) 
                ? (IEnumerable<FileArtifact>)members
                : CollectionUtilities.EmptyArray<FileArtifact>();
        }

        /// <nodoc />
        public PipReference AsPipReference(PipId pipId)
        {
            return new PipReference(PipTable, pipId, PipQueryContext.ViewerAnalyzer);
        }

        #region Log processing

        public override void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
        {
            m_dirData.Add(data.Directory, data);
        }

        /// <inheritdoc />
        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            m_processMonitoringData[data.PipId] = data;
        }

        /// <inheritdoc />
        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            m_processFingerprintData.GetOrAdd(data.PipId, (_) => new Dictionary<FingerprintComputationKind, ProcessFingerprintComputationEventData>()).Add(data.Kind, data);
        }

        /// <inheritdoc />
        public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        {
            foreach (var item in data.DirectoryOutputs)
            {
                m_sharedOpaqueOutputs[item.directoryArtifact] = item.fileArtifactArray;
            }
        }

        /// <inheritdoc />
        public override void BxlInvocation(BxlInvocationEventData data)
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
