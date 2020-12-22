// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeAllowlistAnalyzer()
        {
            string allowlistDirectoryPath = null;
            List<string> logPaths = new List<string>();

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputDirectory", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    allowlistDirectoryPath = ParseSingletonPathOption(opt, allowlistDirectoryPath);
                }
                else if (opt.Name.Equals("al", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("allowlistLog", StringComparison.OrdinalIgnoreCase)||
                   // compatibility
                   opt.Name.Equals("wl", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("whitelistLog", StringComparison.OrdinalIgnoreCase))
                {
                    logPaths.Add(ParsePathOption(opt));
                }
                else
                {
                    throw Error("Unknown option for allowlist analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(allowlistDirectoryPath))
            {
                throw Error("outputDirectory is a required parameter.");
            }

            if (logPaths.Count == 0)
            {
                throw Error("At least one allowlist log path required for allowlist analysis");
            }

            return new AllowlistAnalyzer(GetAnalysisInput())
            {
                LogPaths = logPaths,
                AllowlistDirectoryPath = allowlistDirectoryPath,
            };
        }

        private static void WriteAllowlistAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Allowlist Violation Analysis Generator");
            writer.WriteModeOption(nameof(AnalysisMode.Allowlist), "Generates a file containing file access violation analysis of allowlist entries from a set of log files");
            writer.WriteOption("allowlistLog", "OneOrMany. The path(s) to BuildXL log for build containing allowlisted access log messages. More than one log may be analyzed for cross build analysis.", shortName: "al");
            writer.WriteOption("outputDirectory", "Required. The directory to place allowlist analysis files.", shortName: "o");
        }
    }

    /// <summary>
    /// Analyzer used to generate fingerprint text file
    /// </summary>
    internal sealed class AllowlistAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the allowlist directory
        /// </summary>
        public string AllowlistDirectoryPath;

        public List<string> LogPaths = new List<string>();

        private readonly EnumCounter<FileMonitoringViolationAnalyzer.DependencyViolationType> m_violationTypeCounts = new EnumCounter<FileMonitoringViolationAnalyzer.DependencyViolationType>();

        private readonly Dictionary<long, PipId> m_pipsBySemistableHash = new Dictionary<long, PipId>();

        private MultiWriter m_logWriter;
        private MultiWriter m_allViolationsWriter;
        private MultiWriter m_importantViolationsWriter;

        /// <summary>
        /// The content of each output directory to its corresponding directory artifact owner
        /// </summary>
        /// <remarks>
        /// The rewrite count of a file artifact under an output directory is always one, so keeping the absolute path is enough
        /// </remarks>
        public Dictionary<AbsolutePath, DirectoryArtifact> OutputDirectoryContent = new Dictionary<AbsolutePath, DirectoryArtifact>();

        /// <summary>
        /// Multi-map of path to accesses to the log information defining the access (item1: originating log, item2: originating log line)
        /// </summary>
        private ILookup<AbsolutePath, (string log, string logLine)> m_pathAccessLookup;

        public AllowlistAnalyzer(AnalysisInput input)
            : base(input)
        {
        }

        public override void Prepare()
        {
            foreach (var pipReference in CachedGraph.PipGraph.RetrievePipReferencesOfType(PipType.Process))
            {
                m_pipsBySemistableHash[pipReference.SemiStableHash] = pipReference.PipId;
            }

            AllowlistDirectoryPath = Path.GetFullPath(AllowlistDirectoryPath);
            if (File.Exists(AllowlistDirectoryPath))
            {
                // Can not create a directory if there is an existing file with the same name
                AllowlistDirectoryPath += ".wl";
            }

            Directory.CreateDirectory(AllowlistDirectoryPath);
            Console.WriteLine("Log directory {0}", AllowlistDirectoryPath);

            base.Prepare();
        }

        private ILookup<PipId, (PipId pipId, RequestedAccess requestedAccess, AbsolutePath absolutePath)> ComputeAccesses(MultiWriter logWriter)
        {
            List<(PipId pipId, RequestedAccess requestedAccess, AbsolutePath absolutePath)> accesses = new List<(PipId pipId, RequestedAccess requestedAccess, AbsolutePath absolutePath)>();
            List<(AbsolutePath absolutePath, string logPath, string line)> pathAccesses = new List<(AbsolutePath absolutePath, string logPath, string line)>();

            // TODO: Generally execution log analyzers shouldn't rely on textual log
            // This is here until execution log event for allowlist entries can be added.
            Regex regex = new Regex("DX0269.*\\[Pip(?<pip>\\w*),.*was detected on '(?<fileName>[^']+)' with \\[[^\\]]+\\]\\((?<accessType>[^\\)]+)\\)");

            foreach (var logPath in LogPaths)
            {
                string line = null;
                using (var reader = new StreamReader(logPath))
                using (var reporter = new ProgressReporter((int)reader.BaseStream.Length, (done, total) => Console.WriteLine("Read {0}", reader.BaseStream.Position * 100.0 / (double)total)))
                {
                    reporter.DoneCount = 0;

                    while ((line = reader.ReadLine()) != null)
                    {
                        var match = regex.Match(line);
                        if (match.Success)
                        {
                            var fileName = match.Groups["fileName"].Value;
                            var pip = match.Groups["pip"].Value;
                            var accessType = match.Groups["accessType"].Value;
                            var semistableHash = long.Parse(pip, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            AbsolutePath accessPath;
                            PipId pipId;
                            if (!AbsolutePath.TryCreate(PathTable, fileName, out accessPath))
                            {
                                logWriter.WriteLine("WARNING. Could not parse path: {0}", fileName);
                            }

                            if (!m_pipsBySemistableHash.TryGetValue(semistableHash, out pipId))
                            {
                                logWriter.WriteLine("WARNING. Could not find semi stable hash {0}", semistableHash);
                            }

                            RequestedAccess access;
                            if (!Enum.TryParse<RequestedAccess>(accessType, out access))
                            {
                                logWriter.WriteLine("WARNING. Could not parse access type: {0}", accessType);
                            }

                            accesses.Add((pipId, access, accessPath));
                            pathAccesses.Add((accessPath, logPath, line));
                        }
                    }
                }
            }

            var accessLookup = accesses.ToLookup(v => v.pipId);
            m_pathAccessLookup = pathAccesses.ToLookup(v => v.absolutePath, v => (v.logPath, v.line));
            return accessLookup;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            using (var logFileWriter = new StreamWriter(Path.Combine(AllowlistDirectoryPath, "log.txt")))
            using (var allViolationsWriter = new StreamWriter(Path.Combine(AllowlistDirectoryPath, "AllViolations.txt")))
            using (var importantViolationsWriter = new StreamWriter(Path.Combine(AllowlistDirectoryPath, "ImportantViolations.txt")))
            {
                m_logWriter = new MultiWriter(TextWriter.Synchronized(logFileWriter), Console.Out);
                m_allViolationsWriter = new MultiWriter(allViolationsWriter);
                m_importantViolationsWriter = new MultiWriter(importantViolationsWriter, allViolationsWriter);

                var accessLookup = ComputeAccesses(m_logWriter);

                FileMonitoringViolationAnalyzer analyzer = new AllowlistFileMonitoringViolationAnalyzer(LoggingContext, CachedGraph.Context, CachedGraph.PipGraph, this);

                foreach (var pipReference in CachedGraph.PipGraph.RetrievePipReferencesOfType(PipType.Process))
                {
                    if (accessLookup.Contains(pipReference.PipId))
                    {
                        var process = (Process)pipReference.HydratePip();
                        analyzer.AnalyzePipViolations(
                            process,
                            violations: ConvertAccesses(accessLookup[process.PipId]),
                            allowlistedAccesses: null,
                            exclusiveOpaqueDirectoryContent: ReadOnlyArray<(DirectoryArtifact, ReadOnlyArray<FileArtifactWithAttributes>)>.Empty,
                            sharedOpaqueDirectoryWriteAccesses: null,
                            allowedUndeclaredReads: null,
                            absentPathProbesUnderOutputDirectories: null,
                            ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                            out _);
                        ;
                    }
                }

                m_violationTypeCounts.PrintCounts(m_logWriter);
            }

            return 0;
        }

        /// <inheritdoc/>
        public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        {
            foreach (var kvp in data.DirectoryOutputs)
            {
                foreach (var fileArtifact in kvp.fileArtifactArray)
                {
                    OutputDirectoryContent.Add(fileArtifact.Path, kvp.directoryArtifact);
                }
            }
        }

        private static IReadOnlyCollection<ReportedFileAccess> ConvertAccesses(IEnumerable<(PipId, RequestedAccess, AbsolutePath)> accesses)
        {
            var reportedProcess = new ReportedProcess(10, "thisprocess.exe");
            List<ReportedFileAccess> reportedFileAccesses = new List<ReportedFileAccess>();
            foreach (var access in accesses)
            {
                reportedFileAccesses.Add(new ReportedFileAccess(
                    operation: ReportedFileOperation.CreateFile,
                    process: reportedProcess,
                    requestedAccess: access.Item2,
                    status: FileAccessStatus.None,
                    explicitlyReported: true,
                    error: 0,
                    usn: default(Usn),
                    desiredAccess: DesiredAccess.GENERIC_ALL,
                    shareMode: ShareMode.FILE_SHARE_READ,
                    creationDisposition: CreationDisposition.OPEN_ALWAYS,
                    flagsAndAttributes: FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                    manifestPath: access.Item3,
                    path: null,
                    enumeratePattern: null));
            }

            return reportedFileAccesses;
        }

        public string GetPipDescription(Pip pip)
        {
            if (pip == null)
            {
                return "N/A";
            }

            return pip
                .GetDescription(CachedGraph.Context);
        }

        public string GetPipProvenance(Pip pip)
        {
            if (pip == null)
            {
                return "N/A";
            }

            return pip.Provenance?.Token.Path.ToString(PathTable) ?? "N/A";
        }

        public void HandleDependencyViolation(
            FileMonitoringViolationAnalyzer.DependencyViolationType violationType,
            FileMonitoringViolationAnalyzer.AccessLevel accessLevel,
            AbsolutePath path,
            Pip violator,
            Pip related)
        {
            MultiWriter targetWriter = m_allViolationsWriter;

            m_violationTypeCounts.Increment(violationType);

            if (violationType != FileMonitoringViolationAnalyzer.DependencyViolationType.MissingSourceDependency &&
                violationType != FileMonitoringViolationAnalyzer.DependencyViolationType.UndeclaredOutput)
            {
                targetWriter = m_importantViolationsWriter;
            }

            targetWriter.WriteLine("Violation Type: {0}", violationType);
            targetWriter.WriteLine("Access Level: {0}", accessLevel);
            targetWriter.WriteLine("Path: {0}", path.ToString(PathTable));
            targetWriter.WriteLine("Violator: {0}", GetPipDescription(violator));
            targetWriter.WriteLine("Violator.Location: {0}", GetPipProvenance(violator));

            if (related != null)
            {
                targetWriter.WriteLine("Related: {0}", GetPipDescription(related));
                targetWriter.WriteLine("Related.Location: {0}", GetPipProvenance(related));

                HashSourceFile relatedSourceFile = related as HashSourceFile;
                if (relatedSourceFile != null)
                {
                    var consumers = CachedGraph.PipGraph.GetConsumingPips(relatedSourceFile.Artifact);
                    foreach (var consumer in consumers)
                    {
                        targetWriter.WriteLine("   Related Consumer: {0}", GetPipDescription(consumer));
                        targetWriter.WriteLine("   Related Consumer Location: {0}", GetPipProvenance(consumer));
                    }
                }
            }

            targetWriter.WriteLine("Accesses: ");
            foreach (var pathAccess in m_pathAccessLookup[path])
            {
                targetWriter.WriteLine("   Log Path: {0}", pathAccess.Item1);
                targetWriter.WriteLine("   {0}", pathAccess.Item2);
            }

            targetWriter.WriteLine();
        }

        private sealed class EnumCounter<TEnum>
            where TEnum : System.Enum
        {
            private readonly int[] m_counts;

            public EnumCounter()
            {
                m_counts = new int[EnumTraits<TEnum>.EnumerateValues().Count()];
            }

            public void Increment(TEnum value)
            {
                m_counts[EnumTraits<TEnum>.ToInteger(value)]++;
            }

            public void PrintCounts(MultiWriter logWriter)
            {
                foreach (var value in EnumTraits<TEnum>.EnumerateValues())
                {
                    logWriter.WriteLine("{0}: {1}", value, m_counts[EnumTraits<TEnum>.ToInteger(value)]);
                }

                logWriter.WriteLine();
            }
        }

        private sealed class AllowlistFileMonitoringViolationAnalyzer : FileMonitoringViolationAnalyzer
        {
            private readonly AllowlistAnalyzer m_allowlistAnalyzer;

            public AllowlistFileMonitoringViolationAnalyzer(LoggingContext loggingContext, PipExecutionContext context, PipGraph pipGraph, AllowlistAnalyzer allowlistAnalyzer)
                : base(loggingContext, context, pipGraph, new QueryableFileContentManager(allowlistAnalyzer.OutputDirectoryContent), validateDistribution: false, ignoreDynamicWritesOnAbsentProbes: Utilities.Configuration.DynamicWriteOnAbsentProbePolicy.IgnoreNothing, unexpectedFileAccessesAsErrors: true)
            {
                m_allowlistAnalyzer = allowlistAnalyzer;
            }

            protected override ReportedViolation HandleDependencyViolation(
                DependencyViolationType violationType, 
                AccessLevel accessLevel, 
                AbsolutePath path, 
                Pip violator,
                bool isAllowlistedViolation,
                Pip related,
                AbsolutePath processPath)
            {
                m_allowlistAnalyzer.HandleDependencyViolation(violationType, accessLevel, path, violator, related);

                // Dummy return value
                return new ReportedViolation(false, violationType, path, violator.PipId, related?.PipId, processPath);
            }
        }

        private sealed class QueryableFileContentManager : IQueryableFileContentManager
        {
            private readonly Dictionary<AbsolutePath, DirectoryArtifact> m_outputDirectoryContent;

            public QueryableFileContentManager(Dictionary<AbsolutePath, DirectoryArtifact> outputDirectoryContent)
            {
                m_outputDirectoryContent = outputDirectoryContent;
            }

            public bool TryGetContainingOutputDirectory(AbsolutePath path, out DirectoryArtifact containingOutputDirectory)
            {
                return m_outputDirectoryContent.TryGetValue(path, out containingOutputDirectory);
            }

            public Task<FileContentInfo?> TryQueryUndeclaredInputContentAsync(AbsolutePath path, string consumerDescription = null)
            {
                // There are no allowed undeclared reads at creation time
                return Task.FromResult<FileContentInfo?>(null);
            }
        }
    }
}
