// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Execution.Analyzer.Analyzers.CacheMiss;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using static BuildXL.Utilities.FormattableStringEx;
using StringPair = System.Collections.Generic.KeyValuePair<string, string>;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeCacheMissAnalyzer(AnalysisInput analysisInput)
        {
            string outputDirectory = null;
            bool allPips = false;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputDirectory", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputDirectory = ParseSingletonPathOption(opt, outputDirectory);
                }
                else if (opt.Name.StartsWith("allPips", StringComparison.OrdinalIgnoreCase))
                {
                    allPips = ParseBooleanOption(opt);
                }
                else
                {
                    throw Error("Unknown option for cache miss analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new Exception("'outputDirectory' is required.");
            }

            return new CacheMissAnalyzer(analysisInput)
            {
                OutputDirectory = outputDirectory,
                AllPips = allPips,
            };
        }

        private static void WriteCacheMissHelp(HelpWriter writer)
        {
            writer.WriteBanner("Cache Miss Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.CacheMissLegacy), "Computes cache miss reasons for pips");
            writer.WriteOption("outputDirectory", "Required. The directory where to write the results", shortName: "o");
            writer.WriteOption("allPips", "Optional. Defaults to false.");
        }
    }

    /// <summary>
    /// Analyzer used to compute the reason for cache misses
    /// </summary>
    internal sealed class CacheMissAnalyzer : Analyzer
    {
        public const string AnalysisFileName = "analysis.txt";

        private readonly AnalysisModel m_model;
        private readonly string m_logPath;

        /// <summary>
        /// The path to the output files
        /// </summary>
        public string OutputDirectory;
        public bool AllPips;

        /// <summary>
        /// Any pips that cannot be cached due to file access violations.
        /// </summary>
        public HashSet<PipId> UncacheablePips = new HashSet<PipId>();

        public CacheMissAnalyzer(AnalysisInput input)
            : base(input)
        {
            Console.WriteLine("Loaded old graph");

            m_model = new AnalysisModel(CachedGraph);
            m_logPath = input.ExecutionLogPath;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            return 0;
        }

        internal Analyzer GetDiffAnalyzer(AnalysisInput input)
        {
            Console.WriteLine("Loaded new graph");
            return new DiffAnalyzer(input, this);
        }

        public override bool CanHandleWorkerEvents => true;

        public override void WorkerList(WorkerListEventData data)
        {
            m_model.Workers = data.Workers;
        }

        /// <inheritdoc />
        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            ProcessPipExecutionPerformance performance = data.ExecutionPerformance as ProcessPipExecutionPerformance;
            if (performance != null)
            {
                if (performance.FileMonitoringViolations.HasUncacheableFileAccesses)
                {
                    UncacheablePips.Add(data.PipId);
                }

                if (performance.FileMonitoringViolations.NumFileAccessViolationsNotWhitelisted > 0)
                {
                    // Non-whitelisted pips that have file access violations are not cached.
                    // This can occur in a passing build if UnexpectedFileAccessesAreErrors is disabled.
                    UncacheablePips.Add(data.PipId);
                }
            }
        }

        /// <inheritdoc />
        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            m_model.AddFileContentInfo(CurrentEventWorkerId, data.FileArtifact, data.FileContentInfo);
        }

        /// <inheritdoc />
        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            var pipInfo = m_model.GetPipInfo(data.PipId);
            pipInfo.SetFingerprintComputation(data, CurrentEventWorkerId);
        }

        /// <inheritdoc />
        public override void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
        {
            m_model.AddDirectoryData(CurrentEventWorkerId, data);
        }

        public override void BuildSessionConfiguration(BuildSessionConfigurationEventData data)
        {
            m_model.Salts = data.ToFingerprintSalts();
        }

        private sealed class DiffAnalyzer : Analyzer
        {
            private readonly TextWriter m_writer;
            private readonly AnalysisModel m_oldModel;
            private readonly ConversionModel m_conversionModel;

            private readonly Dictionary<PipId, PipCacheMissType> m_pipMissTypeMap = new Dictionary<PipId, PipCacheMissType>();
            
            /// <summary>
            /// Counts the number of passes over the later build's execution log.
            /// Used to read only certain execution events on the first pass.
            /// </summary>
            private uint m_logPassCount = 0;

            private readonly Dictionary<PipId, bool> m_isHitMap = new Dictionary<PipId, bool>();
            private bool m_hasWrittenMessageForPip;

            private readonly CacheMissAnalyzer m_analyzer;
            private PipId m_currentOldPip;
            private PipId m_currentPip;
            private Process m_currentProcess;
            private Process m_currentOldProcess;

            private PipCachingInfo m_oldInfo;
            private PipCachingInfo m_newInfo;

            private bool m_isDistributed = false;
            private string[] m_workers = new string[] { "Local" };

            private readonly string m_newOutputDirectory;
            private readonly string m_oldOutputDirectory;

            private TextWriter m_oldPipWriter;

            private TextWriter OldPipWriter
            {
                get
                {
                    if (m_oldPipWriter == null)
                    {
                        m_oldPipWriter = new StreamWriter(Path.Combine(m_oldOutputDirectory, GetCurrentSemistableHash() + ".txt"));
                    }

                    return m_oldPipWriter;
                }
            }

            private TextWriter m_newPipWriter;

            private TextWriter NewPipWriter
            {
                get
                {
                    if (m_newPipWriter == null)
                    {
                        m_newPipWriter = new StreamWriter(Path.Combine(m_newOutputDirectory, GetCurrentSemistableHash() + ".txt"));
                    }

                    return m_newPipWriter;
                }
            }

            private readonly Dictionary<string, StringPair> m_currentEnvVarsByName = new Dictionary<string, StringPair>();
            private readonly List<(StringPair envVar1, StringPair EnvVar2)> m_currentChangedEnvVars = new List<(StringPair envVar1, StringPair EnvVar2)>();

            private readonly Dictionary<AbsolutePath, FileData> m_currentDependenciesByPath = new Dictionary<AbsolutePath, FileData>();
            private readonly List<(FileData, FileData)> m_currentChangedDependenciesByPath = new List<(FileData, FileData)>();

            private readonly Dictionary<AbsolutePath, DirectoryArtifact> m_currentDirectoryDependenciesByPath = new Dictionary<AbsolutePath, DirectoryArtifact>();
            private readonly Dictionary<AbsolutePath, DirectoryArtifact> m_currentDirectoryOutputsByPath = new Dictionary<AbsolutePath, DirectoryArtifact>();

            private readonly Dictionary<AbsolutePath, ObservedInput> m_currentInputsByPath = new Dictionary<AbsolutePath, ObservedInput>();
            private readonly List<(ObservedInput observedInput1, ObservedInput observedInput2)> m_currentChangedInputsByPath = new List<(ObservedInput observedInput1, ObservedInput observedInput2)>();

            public DiffAnalyzer(AnalysisInput input, CacheMissAnalyzer analyzer)
            : base(input)
            {
                m_newOutputDirectory = Path.Combine(analyzer.OutputDirectory, "new");
                m_oldOutputDirectory = Path.Combine(analyzer.OutputDirectory, "old");
                Directory.CreateDirectory(analyzer.OutputDirectory);
                Directory.CreateDirectory(m_newOutputDirectory);
                Directory.CreateDirectory(m_oldOutputDirectory);

                m_analyzer = analyzer;
                m_writer = new StreamWriter(Path.Combine(analyzer.OutputDirectory, AnalysisFileName));
                m_oldModel = analyzer.m_model;
                m_conversionModel = new ConversionModel(
                    newGraph: input.CachedGraph,
                    oldGraph: analyzer.CachedGraph,
                    oldModel: m_oldModel);

                m_writer.WriteLine("Comparing executions");
                m_writer.WriteLine(I($"New: {input.ExecutionLogPath}"));
                m_writer.WriteLine(I($"Old: {analyzer.m_logPath}"));
                m_writer.WriteLine();

                m_writer.WriteLine(I($"For details about analyzer output see: {Strings.ExecutionAnalyzer_HelpLink}"));
                m_writer.WriteLine();
                
            }

            public override void Prepare()
            {
                m_logPassCount++;
            }

            public override int Analyze()
            {
                m_writer.Dispose();
                m_oldPipWriter?.Dispose();
                m_newPipWriter?.Dispose();
                return 0;
            }

            public void Write(string message)
            {
                if (!m_hasWrittenMessageForPip)
                {
                    m_hasWrittenMessageForPip = true;
                    if (m_currentOldPip.IsValid)
                    {
                        m_oldModel.MarkChanged(m_currentOldPip);
                    }

                    m_writer.WriteLine(I($"================== Analyzing pip ========================"));
                    m_writer.WriteLine(GetCurrentProcessDescription());
                    if (m_currentOldPip.IsValid)
                    {
                        if (m_oldModel.HasChangedDependencies(m_currentOldPip))
                        {
                            m_writer.WriteLine(I($"Pip has upstream dependencies that were executed."));
                        }
                    }

                    if (m_isDistributed)
                    {
                        m_writer.WriteLine(I($"Worker Info:"));
                        var oldWorker = m_oldInfo != null ? m_oldInfo.Model.Workers[m_oldInfo.WorkerId] : "Unknown";
                        var newWorker = m_newInfo != null ? m_newInfo.Model.Workers[m_newInfo.WorkerId] : "Unknown";
                        m_writer.WriteLine(I($"New: {newWorker}"));
                        m_writer.WriteLine(I($"Old: {oldWorker}"));
                    }

                    m_writer.WriteLine();
                }

                m_writer.WriteLine(message);
            }

            private string GetDescription(PipId pipId)
            {
                return m_oldModel.PipTable.HydratePip(pipId, PipQueryContext.ViewerAnalyzer).GetDescription(m_oldModel.CachedGraph.Context);
            }

            private Process GetCurrentProcess()
            {
                m_currentProcess = m_currentProcess ?? (Process)PipTable.HydratePip(m_currentPip, PipQueryContext.ViewerAnalyzer);
                return m_currentProcess;
            }

            private Process GetCurrentOldProcess()
            {
                m_currentOldProcess = m_currentOldProcess ?? (Process)m_oldModel.PipTable.HydratePip(m_currentOldPip, PipQueryContext.ViewerAnalyzer);
                return m_currentOldProcess;
            }

            private string GetCurrentProcessDescription()
            {
                return GetCurrentProcess().GetDescription(PipGraph.Context);
            }

            private string GetCurrentSemistableHash()
            {
                return GetCurrentProcess().SemiStableHash.ToString("x16", CultureInfo.InvariantCulture);
            }

            /// <summary>
            /// Whether events of the given event id should be read in the first pass of the later build's execution log
            /// </summary>
            public bool IsFirstPassEvent(ExecutionEventId eventId)
            {
               switch(eventId)
               {
                    case ExecutionEventId.PipCacheMiss:
                    case ExecutionEventId.FileArtifactContentDecided:
                    case ExecutionEventId.DirectoryMembershipHashed:
                        return true;
                    default:
                        return false;
               }
            }

            public override bool CanHandleWorkerEvents => true;

            public override bool CanHandleEvent(ExecutionEventId eventId, uint workerId, long timestamp, int eventPayloadSize)
            {
                // On first pass, just read in information about what caused cache misses to use future passes
                if (m_logPassCount == 1)
                {
                    return IsFirstPassEvent(eventId);
                }
                
                return IsFirstPassEvent(eventId) ? false : base.CanHandleEvent(eventId, workerId, timestamp, eventPayloadSize);
            }

            public override void WorkerList(WorkerListEventData data)
            {
                m_isDistributed = data.Workers.Length > 1;
                m_conversionModel.ConvertedNewModel.Workers = data.Workers;
            }

            /// <inheritdoc />
            public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
            {
                m_conversionModel.FileArtifactContentDecided(CurrentEventWorkerId, data);
            }

            /// <inheritdoc />
            public override void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
            {
                m_conversionModel.DirectoryMembershipHashed(CurrentEventWorkerId, data);
            }

            /// <inheritdoc />
            public override void BuildSessionConfiguration(BuildSessionConfigurationEventData data)
            {
                m_conversionModel.ConvertedNewModel.Salts = data.ToFingerprintSalts();
            }

            private void Reset(PipId pipId)
            {
                m_currentDirectoryDependenciesByPath.Clear();
                m_currentDirectoryOutputsByPath.Clear();
                m_oldPipWriter?.Dispose();
                m_newPipWriter?.Dispose();

                m_oldPipWriter = null;
                m_newPipWriter = null;

                if (m_hasWrittenMessageForPip)
                {
                    m_writer.WriteLine("================== Complete pip ========================");
                    m_writer.WriteLine();
                    m_writer.WriteLine();
                }

                m_currentOldPip = m_conversionModel.Convert(pipId);

                m_currentPip = pipId;
                m_currentProcess = null;
                m_currentOldProcess = null;
                m_hasWrittenMessageForPip = false;
            }

            public override void PipExecutionStepPerformanceReported(PipExecutionStepPerformanceEventData data)
            {
                base.PipExecutionStepPerformanceReported(data);
            }

            public override void PipCacheMiss(PipCacheMissEventData data)
            {
                m_pipMissTypeMap.Add(data.PipId, data.CacheMissType);
            }

            /// <summary>
            /// Occurs when a new fingerprint is put into the cache after executing a pip.
            /// </summary>
            public void ProcessFingerprintComputedForExecution(ProcessFingerprintComputationEventData data)
            {
                if (!m_currentOldPip.IsValid)
                {
                    Write($"Pip is missing from old graph.");
                }
                else
                {
                    m_oldModel.MarkChanged(m_currentOldPip);
                }

                if (!m_isDistributed)
                {
                    bool isHit;
                    if (!m_isHitMap.TryGetValue(data.PipId, out isHit))
                    {
                        Write($"Execution detected for pip with no corresponding cache check");
                    }
                }
            }

            /// <summary>
            /// Occurs when a fingerprint is computed to query the cache for an entry. This determines whether a pip is executed.
            /// </summary>
            public void ProcessFingerprintComputedForCacheLookup(ProcessFingerprintComputationEventData data)
            {
                // Cache hit, do nothing
                if (!m_pipMissTypeMap.ContainsKey(data.PipId))
                {
                    return;
                }

                if (!m_currentOldPip.IsValid)
                {
                    // Pip missing from old graph
                    // Wait for subsequent fingerprint computation event from pip execution to write console output
                    return;
                }

                // Don't analyze downstream pips of a pip that was already analyzed
                // (unless AllPips is enabled)
                if (!m_analyzer.AllPips && m_oldModel.HasChangedDependencies(m_currentOldPip))
                {
                    return;
                }

                PipCacheMissType cacheMissType = m_pipMissTypeMap[data.PipId];
                Write($"Cache miss type: {cacheMissType}");
                switch (cacheMissType)
                {
                    // We had a weak and strong fingerprint match, but couldn't retrieve correct data from the cache
                    case PipCacheMissType.MissForCacheEntry:
                    case PipCacheMissType.MissForProcessMetadata:
                    case PipCacheMissType.MissForProcessMetadataFromHistoricMetadata:
                    case PipCacheMissType.MissForProcessOutputContent:
                        Write($"Data missing from the cache.");
                        return;
                    case PipCacheMissType.MissDueToInvalidDescriptors:
                        Write($"Cache returned invalid data.");
                        return;
                    case PipCacheMissType.MissForDescriptorsDueToArtificialMissOptions:
                        Write($"Cache miss artificially forced by user.");
                        return;
                    case PipCacheMissType.Invalid:
                        Write($"Unexpected condition! No valid changes or cache issues were detected to cause process execution, but a process still executed.");
                        return;
                }

                // Pips that could not be cached in the previous build due to file access violations
                if (m_analyzer.UncacheablePips.Contains(m_conversionModel.Convert(data.PipId)))
                {
                    Write($"Caching was prevented for this pip because of disallowed file accesses in the previous build");
                    return;
                }

                // There is a fingerprint mismatch, setup data for comparing fingerprints
                var oldInfo = m_oldModel.GetPipInfo(m_currentOldPip);
                m_oldInfo = oldInfo;
                if (!oldInfo.HasValidFingerprintComputation)
                {
                    Write($"No fingerprint computation data found to compare to since pip was skipped or filtered out in previous build.");
                    m_oldModel.MarkChanged(m_currentOldPip);

                    return;
                }

                m_conversionModel.ProcessFingerprintComputed(CurrentEventWorkerId, data);
                var newInfo = m_conversionModel.GetPipInfo(GetCurrentProcess());
                m_newInfo = newInfo;
                
                if (oldInfo == null || newInfo == null)
                {
                    // This condition should never happen if the PipId has already been verified as valid
                    Write($"Unable to find pip in old graph.");
                    m_oldModel.MarkChanged(m_currentOldPip);

                    // Report that pip executed because it was not run in the original graph
                    return;
                }

                if (cacheMissType == PipCacheMissType.MissForDescriptorsDueToWeakFingerprints)
                {
                    // Compare weak fingerprints
                    if (newInfo.FingerprintComputation.WeakFingerprint != oldInfo.FingerprintComputation.WeakFingerprint)
                    {
                        WriteWeakFingerprintData(newInfo: newInfo, oldInfo: oldInfo);
                        Write(I($"Weak Fingerprint mismatch detected:"));
                        Write(I($"New: {newInfo.FingerprintComputation.WeakFingerprint}"));
                        Write(I($"Old: {oldInfo.FingerprintComputation.WeakFingerprint}"));

                        EqualCompare(newInfo.GetEnvironmentVariables(), oldInfo.GetEnvironmentVariables(), "Environment Variables", Print, ExtractChangedInputs);
                        EqualCompare(newInfo.DependencyData, oldInfo.DependencyData, "File Dependency", Print, ExtractChangedInputs);
                        EqualCompare(newInfo.CacheablePipInfo.DirectoryDependencies.SelectList(d => d.Path), oldInfo.CacheablePipInfo.DirectoryDependencies.SelectList(d => d.Path), "Directory Dependency", Print);
                        EqualCompare(newInfo.CacheablePipInfo.Outputs, oldInfo.CacheablePipInfo.Outputs, "File Outputs", Print);
                        EqualCompare(newInfo.CacheablePipInfo.DirectoryOutputs.ToPathList(), oldInfo.CacheablePipInfo.DirectoryOutputs.ToPathList(), "Directory Outputs", Print);
                        return;
                    }
                }
                else if (cacheMissType == PipCacheMissType.MissForDescriptorsDueToStrongFingerprints)
                {
                    if (!oldInfo.FingerprintComputation.TryGetUsedStrongFingerprintComputation(out ProcessStrongFingerprintComputationData oldStrongFingerprintComputation))
                    {
                        Write("No strong fingerprints were stored during the first build, causing a cache miss. The pip may have failed.");
                        return;
                    }

                    ProcessStrongFingerprintComputationData newStrongFingerprintComputation;
                    if (!newInfo.FingerprintComputation.TryGetComputationWithPathSet(
                        oldStrongFingerprintComputation.PathSetHash,
                        out newStrongFingerprintComputation))
                    {
                        Write(I($"Unable to find the first build's computation for pathset in the current build:"));
                        Write(I($"First build pathset strong fingerprint hash: {oldStrongFingerprintComputation.PathSetHash}"));
                        Write(I($"Derived from paths:"));
                        foreach (ObservedInput pathInput in oldStrongFingerprintComputation.ObservedInputs)
                        {
                            Write(I($"  Path: '{pathInput.Path}' hash: {pathInput.Hash}"));
                        }

                        Write(I($"All new build strong fingerprint calculations:"));
                        foreach (ProcessStrongFingerprintComputationData computation in newInfo.FingerprintComputation.StrongFingerprintComputations)
                        {
                            Write($"  Strong fingerprint: {computation.ComputedStrongFingerprint}");
                            foreach (ObservedInput pathInput in computation.ObservedInputs)
                            {
                                Write(I($"    Path: '{pathInput.Path}' hash: {pathInput.Hash}"));
                            }
                        }

                        return;
                    }

                    if (!newStrongFingerprintComputation.Succeeded)
                    {
                        Write(I($"Computation of strong fingerprint failed in New execution cache check"));
                        Write(I($"  This occurs if graph/policy was changed such that pip is no longer allowed "));
                        Write(I($"  to access a path contained in the prior observed inputs (i.e. dependent seal directory contents"));
                        Write(I($"  no longer contain the designated path)."));
                        return;
                    }

                    if (!newStrongFingerprintComputation.HasPriorStrongFingerprint(oldStrongFingerprintComputation.ComputedStrongFingerprint))
                    {
                        Write(I($"Strong fingerprint ({oldStrongFingerprintComputation.ComputedStrongFingerprint}) from prior build NOT encountered during cache lookup"));
                        Write(string.Empty);
                    }


                    if (newStrongFingerprintComputation.ComputedStrongFingerprint != oldStrongFingerprintComputation.ComputedStrongFingerprint)
                    {
                        Write("Strong Fingerprint mismatch detected:");

                        newInfo.StrongFingerprintComputation = newStrongFingerprintComputation;
                        oldInfo.StrongFingerprintComputation = oldStrongFingerprintComputation;
                        WriteStrongFingerprintData(newInfo: newInfo, oldInfo: oldInfo);

                        Write(I($"New: {newStrongFingerprintComputation.ComputedStrongFingerprint}"));
                        Write(I($"Old: {oldStrongFingerprintComputation.ComputedStrongFingerprint}"));

                        m_currentChangedInputsByPath.Clear();

                        var newUnsafeOptions = newStrongFingerprintComputation.UnsafeOptions;
                        var oldUnsafeOptions = oldStrongFingerprintComputation.UnsafeOptions;
                        if (!oldUnsafeOptions.IsAsSafeOrSaferThan(newUnsafeOptions))
                        {
                            Write(I($"Safer execution options were used compared to prior execution."));

                            var props = typeof(IUnsafeSandboxConfiguration).GetProperties();
                            Func<IUnsafeSandboxConfiguration, string[]> mapPropertyValuesToStrings = (obj) =>
                            {
                                Func<object, string> toStrFn = (o) =>
                                    o == null ? "null" :
                                    o is IEnumerable<string> ? string.Join(", ", o as IEnumerable<string>) :
                                    o.ToString();
                                return props.SelectArray(p => $"{p.Name}: {toStrFn(p.GetValue(obj))}");
                            };

                            EqualCompare(
                                mapPropertyValuesToStrings(newUnsafeOptions.UnsafeConfiguration),
                                mapPropertyValuesToStrings(oldUnsafeOptions.UnsafeConfiguration),
                                "Unsafe Configuration",
                                toString: x => x);

                            EqualCompare(
                                new[] { newUnsafeOptions.PreserveOutputsSalt },
                                new[] { oldUnsafeOptions.PreserveOutputsSalt },
                                "Preserve Outputs Salt",
                                h => h.ToString());
                        }

                        // Compare observed inputs
                        if (EqualCompare(
                                newStrongFingerprintComputation.ObservedInputs,
                                oldStrongFingerprintComputation.ObservedInputs,
                                "Observed Inputs",
                                DiffPrint,
                                ExtractChangedInputs))
                        {
                            // TODO: Emit pip graph filesystem existence

                            // Observed inputs are equal so just continue
                            return;
                        }

                        if (m_currentChangedInputsByPath.Count != 0)
                        {
                            bool wroteHeader = false;
                            foreach (var changedObservedInput in m_currentChangedInputsByPath)
                            {
                                if (changedObservedInput.observedInput1.Type == ObservedInputType.DirectoryEnumeration &&
                                    changedObservedInput.observedInput2.Type == ObservedInputType.DirectoryEnumeration)
                                {
                                    var path = changedObservedInput.observedInput1.Path;
                                    var oldMembers = oldInfo.GetDirectoryMembershipData(path, changedObservedInput.observedInput1.PathEntry.EnumeratePatternRegex);
                                    var newMembers = newInfo.GetDirectoryMembershipData(path, changedObservedInput.observedInput2.PathEntry.EnumeratePatternRegex);

                                    WriteOnce(ref wroteHeader, "Changed Directory Dependencies:");
                                    Write(I($"Directory: {Print(path)}"));

                                    if (oldMembers == null)
                                    {
                                        Write("Old: No membership info found for old graph");
                                    }

                                    if (newMembers == null)
                                    {
                                        Write("New: No membership info found for new graph");
                                    }

                                    if (newMembers != null && oldMembers != null)
                                    {
                                        EqualCompare(
                                            newMembers.Value.Members,
                                            oldMembers.Value.Members,
                                            "Members:",
                                            Print);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
            {
                Reset(data.PipId);

                if (data.Kind == FingerprintComputationKind.Execution)
                {
                    ProcessFingerprintComputedForExecution(data);
                }
                else
                {
                    if (!m_isDistributed)
                    {
                        // Record cache lookup for the pip and the result
                        m_isHitMap[data.PipId] = data.IsHit();
                    }

                    ProcessFingerprintComputedForCacheLookup(data);
                }
            }

            private void WriteWeakFingerprintData(PipCachingInfo newInfo, PipCachingInfo oldInfo)
            {
                WriteWeakFingerprintData(newInfo, NewPipWriter);
                WriteWeakFingerprintData(oldInfo, OldPipWriter);
            }

            private static void WriteWeakFingerprintData(PipCachingInfo info, TextWriter writer)
            {
                writer.WriteLine("Weak Fingerprint Info");
                writer.WriteLine(I($"Weak Fingerprint: {info.FingerprintComputation.WeakFingerprint}"));

                string fingerprintText;
                var fingerprint = info.Fingerprinter.ComputeWeakFingerprint(info.GetOriginalProcess(), out fingerprintText);

                if (fingerprint.Hash != info.FingerprintComputation.WeakFingerprint.Hash)
                {
                    writer.WriteLine(I($"Weak Fingerprint (From Analyzer): {fingerprint}"));
                    writer.WriteLine("WARNING: Weak fingerprint computed by analyzer does not match the weak fingerprint retrieved from the log.");
                    writer.WriteLine("This may be due to a mismatch in the BuildXL version or even a bug in the analyzer.");
                }

                writer.WriteLine();
                writer.WriteLine("Fingerprint Text:");
                writer.WriteLine(fingerprintText);
            }

            private void WriteStrongFingerprintData(PipCachingInfo newInfo, PipCachingInfo oldInfo)
            {
                WriteStrongFingerprintData(newInfo, NewPipWriter);
                WriteStrongFingerprintData(oldInfo, OldPipWriter);
            }

            private void WriteStrongFingerprintData(PipCachingInfo info, TextWriter writer)
            {
                writer.WriteLine("Strong Fingerprint Info");
                writer.WriteLine(I($"PathSet Hash: {info.StrongFingerprintComputation.PathSetHash}"));
                writer.WriteLine();
                writer.WriteLine("Path Set:");
                writer.WriteLine();
                foreach (var entry in info.StrongFingerprintComputation.PathEntries)
                {
                    writer.WriteLine(Print(entry));
                }

                writer.WriteLine(I($"Strong Fingerprint: {info.StrongFingerprintComputation.ComputedStrongFingerprint}"));
                writer.WriteLine();
                writer.WriteLine("Observed Inputs:");
                writer.WriteLine();
                foreach (var observedInput in info.StrongFingerprintComputation.ObservedInputs)
                {
                    writer.WriteLine(Print(observedInput));

                    if (observedInput.Type != ObservedInputType.FileContentRead)
                    {
                        var path = observedInput.Path;

                        if (observedInput.Type == ObservedInputType.DirectoryEnumeration)
                        {
                            var membershipData = info.GetDirectoryMembershipData(observedInput.Path, observedInput.PathEntry.EnumeratePatternRegex);
                            if (!membershipData.HasValue)
                            {
                                writer.WriteLine($"  DirectoryEnumeration membershipData is null.  This may be a race condition.  Contact domdev for more info.");
                                continue;
                            }

                            writer.WriteLine($"  Flags: {string.Join(" | ", GetFlags(membershipData.Value))}");
                            writer.WriteLine($"  EnumeratePatternRegex: {membershipData.Value.EnumeratePatternRegex}");
                            writer.WriteLine($"  Members:{membershipData.Value.Members.Count}"); 
                            
                            membershipData.Value.Members.Sort(m_oldModel.PathTable.ExpandedPathComparer);
                            foreach (var item in membershipData.Value.Members)
                            {
                                string relativePath;
                                if (m_oldModel.PathTable.TryExpandNameRelativeToAnother(path.Value, item.Value, out relativePath))
                                {
                                    writer.WriteLine("  " + relativePath);
                                }
                                else
                                {
                                    writer.WriteLine("  " + path.ToString(m_oldModel.PathTable));
                                }
                            }
                        }
                    }
                }
            }

            public static IEnumerable<string> GetFlags(DirectoryMembershipHashedEventData dirData)
            {
                yield return dirData.IsStatic ? "static" : "dynamic";

                if (dirData.IsSearchPath)
                {
                    yield return "search path";
                }
            }

            private string DiffPrint(ObservedInput arg)
            {
                return Print(arg)
                    + Environment.NewLine +
                    I($"Seal directory parent: {GetDirectoryParent(arg.Path)}");
            }

            private string GetDirectoryParent(AbsolutePath path)
            {
                while (path.IsValid)
                {
                    DirectoryArtifact directory;
                    if (m_currentDirectoryDependenciesByPath.TryGetValue(path, out directory))
                    {
                            return "(dependency) " + GetDescription(m_oldModel.CachedGraph.PipGraph.GetSealedDirectoryNode(directory).ToPipId());
                    }

                    if (m_currentDirectoryOutputsByPath.TryGetValue(path, out directory))
                    {
                        return "(output) " + GetDescription(m_oldModel.CachedGraph.PipGraph.GetSealedDirectoryNode(directory).ToPipId());
                    }

                    path = path.GetParent(m_oldModel.PathTable);
                }

                return "None";
            }

            private string Print(StringPair arg)
            {
                return I($"{arg.Key}={arg.Value}");
            }

            private string Print(ObservedInput arg)
            {
                return I($"{arg.Type}: {Print(arg.Path)} (Hash = {arg.Hash.ToString()}, Flags = {PrintFlags(arg.PathEntry)})");
            }

            private string Print(ObservedPathEntry arg)
            {
                return I($"{Print(arg.Path)} (Flags = {PrintFlags(arg)})");
            }

            private string Print(FileData arg)
            {
                return I($"{Print(arg.File)} [{arg.Hash}]");
            }

            private string Print(AbsolutePath path)
            {
                try
                {
                    return path.IsValid ? path.ToString(m_oldModel.PathTable) : "<Unknown>";
                }
#pragma warning disable ERP022 // TODO: This should really handle specific errors
                catch
                {
                    return "<Unknown>";
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
            }

            private static string PrintFlags(ObservedPathEntry arg)
            {
                return string.Join(" | ", GetFlags(arg));
            }

            private static IEnumerable<string> GetFlags(ObservedPathEntry arg)
            {
                if (arg.IsSearchPath)
                {
                    yield return "SearchPath";
                }

                if (arg.IsDirectoryPath)
                {
                    yield return "DirectoryPath";
                }
            }

            private string Print(FileArtifactWithAttributes arg)
            {
                return I($"{Print(arg.Path)} ({arg.RewriteCount})");
            }

            private string Print(FileArtifact arg)
            {
                return I($"{Print(arg.Path)} ({arg.RewriteCount})");
            }

            private string Print(DirectoryArtifact arg)
            {
                return I($"{Print(arg.Path)} ({arg.GetHashCode()})");
            }

            private void ExtractChangedInputs(
                ref IEnumerable<StringPair> added,
                ref IEnumerable<StringPair> removed,
                out IEnumerable<(StringPair, StringPair)> changed)
            {
                m_currentEnvVarsByName.Clear();
                m_currentChangedEnvVars.Clear();

                foreach (var item in added)
                {
                    m_currentEnvVarsByName[item.Key] = item;
                }

                foreach (var item in removed)
                {
                    StringPair currentInput;
                    if (m_currentEnvVarsByName.TryGetValue(item.Key, out currentInput))
                    {
                        m_currentChangedEnvVars.Add((currentInput, item));
                        m_currentEnvVarsByName.Remove(item.Key);
                    }
                    else
                    {
                        m_currentEnvVarsByName[item.Key] = item;
                    }
                }

                added = added.Where(item => m_currentEnvVarsByName.ContainsKey(item.Key));
                removed = removed.Where(item => m_currentEnvVarsByName.ContainsKey(item.Key));
                changed = m_currentChangedEnvVars;
            }

            private void ExtractChangedInputs(
                ref IEnumerable<FileData> added,
                ref IEnumerable<FileData> removed,
                out IEnumerable<(FileData, FileData)> changed)
            {
                m_currentDependenciesByPath.Clear();
                m_currentChangedDependenciesByPath.Clear();

                foreach (var item in added)
                {
                    m_currentDependenciesByPath[item.Path] = item;
                }

                foreach (var item in removed)
                {
                    FileData currentInput;
                    if (m_currentDependenciesByPath.TryGetValue(item.Path, out currentInput))
                    {
                        m_currentChangedDependenciesByPath.Add((currentInput, item));
                        m_currentDependenciesByPath.Remove(item.Path);
                    }
                    else
                    {
                        m_currentDependenciesByPath[item.Path] = item;
                    }
                }

                added = added.Where(item => m_currentDependenciesByPath.ContainsKey(item.Path));
                removed = removed.Where(item => m_currentDependenciesByPath.ContainsKey(item.Path));
                changed = m_currentChangedDependenciesByPath;
            }

            private void ExtractChangedInputs(
                ref IEnumerable<ObservedInput> added,
                ref IEnumerable<ObservedInput> removed,
                out IEnumerable<(ObservedInput, ObservedInput)> changed)
            {
                m_currentDirectoryDependenciesByPath.Clear();
                var currentOldProcess = GetCurrentOldProcess();
                if (currentOldProcess != null)
                {
                    foreach (var directory in currentOldProcess.DirectoryDependencies)
                    {
                        m_currentDirectoryDependenciesByPath[directory.Path] = directory;
                    }

                    foreach (var directory in currentOldProcess.DirectoryOutputs)
                    {
                        m_currentDirectoryOutputsByPath[directory.Path] = directory;
                    }
                }

                m_currentInputsByPath.Clear();
                m_currentChangedInputsByPath.Clear();

                foreach (var item in added)
                {
                    m_currentInputsByPath[item.Path] = item;
                }

                foreach (var item in removed)
                {
                    ObservedInput currentInput;
                    if (m_currentInputsByPath.TryGetValue(item.Path, out currentInput))
                    {
                        m_currentChangedInputsByPath.Add((currentInput, item));
                        m_currentInputsByPath.Remove(item.Path);
                    }
                    else
                    {
                        m_currentInputsByPath[item.Path] = item;
                    }
                }

                added = added.Where(item => m_currentInputsByPath.ContainsKey(item.Path));
                removed = removed.Where(item => m_currentInputsByPath.ContainsKey(item.Path));
                changed = m_currentChangedInputsByPath;
            }

            private delegate void ExtractChanged<T>(ref IEnumerable<T> added, ref IEnumerable<T> removed, out IEnumerable<(T, T)> changed);

            private bool EqualCompare<T>(
                IReadOnlyList<T> current,
                IReadOnlyList<T> prior,
                string header,
                Func<T, string> toString,
                ExtractChanged<T> extractChanged = null,
                bool orderIndependent = false)
            {
                header += " Differences";

                bool notEqual = false;
                var removed = prior.Except(current);
                var added = current.Except(prior);

                var changed = Enumerable.Empty<(T, T)>();
                extractChanged?.Invoke(ref added, ref removed, out changed);
                if (changed != null)
                {
                    foreach (var item in changed)
                    {
                        WriteOnce(ref notEqual, header);
                        Write("Changed:");
                        Write(I($"New: {toString(item.Item1)}"));
                        Write(I($"Old: {toString(item.Item2)}"));
                    }
                }

                foreach (var item in removed)
                {
                    WriteOnce(ref notEqual, header);
                    Write(I($"Removed: {toString(item)}"));
                }

                foreach (var item in added)
                {
                    WriteOnce(ref notEqual, header);
                    Write(I($"Added: {toString(item)}"));
                }

                if (!orderIndependent && !notEqual)
                {
                    int count = Math.Min(current.Count, prior.Count);
                    for (int i = 0; i < count; i++)
                    {
                        if (!EqualityComparer<T>.Default.Equals(current[i], prior[i]))
                        {
                            WriteOnce(ref notEqual, header);
                            Write(I($"Order Changed New: {toString(current[i])}"));
                            Write(I($"Order Changed Old: {toString(prior[i])}"));
                            break;
                        }
                    }
                }

                return !notEqual;
            }

            private void WriteOnce(ref bool printed, string message)
            {
                if (!printed)
                {
                    printed = true;
                    Write(message);
                }
            }
        }
    }
}
