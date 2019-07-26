// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeDumpPipAnalyzer()
        {
            string outputFilePath = null;
            long semiStableHash = 0;
            bool useOriginalPaths = false;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals("pip", StringComparison.OrdinalIgnoreCase) ||
                         opt.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    semiStableHash = ParseSemistableHash(opt);
                }
                else if (opt.Name.Equals("useOriginalPaths", StringComparison.OrdinalIgnoreCase) ||
                         opt.Name.Equals("u", StringComparison.OrdinalIgnoreCase))
                {
                    useOriginalPaths = ParseBooleanOption(opt);
                }
                else
                {
                    throw Error("Unknown option for dump pip analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(outputFilePath))
            {
                throw Error("/outputFile parameter is required");
            }

            if (semiStableHash == 0)
            {
                throw Error("/pip parameter is required");
            }

            return new DumpPipAnalyzer(GetAnalysisInput(), outputFilePath, semiStableHash, useOriginalPaths);
        }

        private static void WriteDumpPipAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Dump Pip Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.DumpPip), "Generates an html file containing information about the requested pip");
            writer.WriteOption("outputFile", "Required. The location of the output file for critical path analysis.", shortName: "o");
            writer.WriteOption("pip", "Required. The formatted semistable hash of a pip to dump (must start with 'Pip', e.g., 'PipC623BCE303738C69')");
        }
    }

    /// <summary>
    /// Exports a JSON structured graph, including per-pip static and execution details.
    /// </summary>
    public sealed class DumpPipAnalyzer : Analyzer
    {
        private readonly Pip m_pip;
        private readonly HtmlHelper m_html;

        private readonly string m_outputFilePath;

        private readonly List<XElement> m_sections = new List<XElement>();
        private readonly Dictionary<ModuleId, string> m_moduleIdToFriendlyName = new Dictionary<ModuleId, string>();
        private readonly ConcurrentBigMap<DirectoryArtifact, IReadOnlyList<FileArtifact>> m_directoryContents = new ConcurrentBigMap<DirectoryArtifact, IReadOnlyList<FileArtifact>>();

        private DominoInvocationEventData m_invocationData;
        private readonly bool m_useOriginalPaths;

        public DumpPipAnalyzer(AnalysisInput input, string outputFilePath, long semiStableHash, bool useOriginalPaths, bool logProgress = false)
            : base(input)
        {
            m_outputFilePath = outputFilePath;
            m_useOriginalPaths = useOriginalPaths;

            if (logProgress)
            {
                Console.WriteLine("Finding matching pip");
            }

            var pipTable = input.CachedGraph.PipTable;
            foreach (var pipId in pipTable.StableKeys)
            {
                if (pipTable.GetPipType(pipId) == PipType.Module)
                {
                    var modulePip = (ModulePip)pipTable.HydratePip(pipId, PipQueryContext.ViewerAnalyzer);
                    m_moduleIdToFriendlyName.Add(modulePip.Module, modulePip.Identity.ToString(StringTable));
                }

                var possibleMatch = pipTable.GetPipSemiStableHash(pipId);
                if (possibleMatch == semiStableHash)
                {
                    m_pip = pipTable.HydratePip(pipId, PipQueryContext.ViewerAnalyzer);
                }
            }

            m_html = new HtmlHelper(PathTable, StringTable, SymbolTable, CachedGraph.PipTable);
        }

        public XDocument GetXDocument() {
            if (m_pip == null)
            {
                Console.Error.WriteLine("Did not find matching pip");
                return null;
            }

            var basicRows = new List<object>();
            basicRows.Add(m_html.CreateRow("PipId", m_pip.PipId.Value.ToString(CultureInfo.InvariantCulture) + " (" + m_pip.PipId.Value.ToString("X16", CultureInfo.InvariantCulture) + ")"));
            basicRows.Add(m_html.CreateRow("SemiStableHash", m_pip.SemiStableHash.ToString("X16")));
            basicRows.Add(m_html.CreateRow("Pip Type", m_pip.PipType.ToString()));
            basicRows.Add(m_html.CreateRow("Tags", m_pip.Tags.IsValid ? m_pip.Tags.Select(tag => tag.ToString(StringTable)) : null));

            var provenance = m_pip.Provenance;
            if (provenance != null) {
                basicRows.Add(m_html.CreateRow("Qualifier", PipGraph.Context.QualifierTable.GetCanonicalDisplayString(provenance.QualifierId)));
                basicRows.Add(m_html.CreateRow("Usage", provenance.Usage));
                basicRows.Add(m_html.CreateRow("Spec", provenance.Token.Path));
                basicRows.Add(m_html.CreateRow("Location", provenance.Token));
                basicRows.Add(m_html.CreateRow("Thunk", provenance.OutputValueSymbol));
                basicRows.Add(m_html.CreateRow("ModuleId", GetModuleName(provenance.ModuleId)));
            }


            var main = new XElement(
                "div",
                new XElement(
                    "div",
                    m_html.CreateBlock(
                        "Pip Metadata",
                        basicRows),
                    GetPipSpecificDetails(m_pip),
                    m_html.CreateBlock(
                        "Static Pip Dependencies",
                        m_html.CreateRow(
                            "Pip Dependencies",
                            CachedGraph
                                .PipGraph
                                .RetrievePipReferenceImmediateDependencies(m_pip.PipId, null)
                                .Where(pipRef => pipRef.PipType != PipType.HashSourceFile)
                                .Select(pipRef => pipRef.PipId)),
                        m_html.CreateRow(
                            "Source Dependencies",
                            CachedGraph
                                .PipGraph
                                .RetrievePipReferenceImmediateDependencies(m_pip.PipId, null)
                                .Where(pipRef => pipRef.PipType == PipType.HashSourceFile)
                                .Select(pipRef => pipRef.PipId)),
                        m_html.CreateRow(
                            "Pip Dependents",
                            CachedGraph
                                .PipGraph
                                .RetrievePipReferenceImmediateDependents(m_pip.PipId, null)
                                .Select(pipRef => pipRef.PipId))),
                    m_sections));

            var doc = m_html.CreatePage("Pip Details for Pip" + m_pip.SemiStableHash.ToString("X16"), main);
            return doc;
        }

        public override int Analyze()
        {          
            var doc = GetXDocument();
            if (doc == null)
            {
                return 1;
            }

            var loggingConfig = m_invocationData.Configuration.Logging;

            if (m_useOriginalPaths &&
                loggingConfig.SubstTarget.IsValid &&
                loggingConfig.SubstSource.IsValid
                )
            {
                // tostring on root of drive automatically adds trailing slash, so only add trailing slash when needed.
                var target = loggingConfig.SubstTarget.ToString(PathTable, PathFormat.HostOs);
                if (target.LastOrDefault() != Path.DirectorySeparatorChar)
                {
                    target += Path.DirectorySeparatorChar;
                }

                var source = loggingConfig.SubstSource.ToString(PathTable, PathFormat.HostOs);
                if (source.LastOrDefault() != Path.DirectorySeparatorChar)
                {
                    source += Path.DirectorySeparatorChar;
                }

                // Instead of doing the proper replacement at every path emission, taking a very blunt shortcut here by doing string replace.
                var html = doc.ToString();
                var updatedHtml = html.Replace(target, source);
                File.WriteAllText(m_outputFilePath, updatedHtml);
                return 0;
            }

            doc.Save(m_outputFilePath);
            return 0;
        }

        public override void DominoInvocation(DominoInvocationEventData data)
        {
            m_invocationData = data;
        }

        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            if (data.PipId == m_pip.PipId)
            {
                m_sections.Add(
                    m_html.CreateBlock(
                        "Pip Execution Performance",
                        m_html.CreateRow("Execution Start", data.ExecutionPerformance.ExecutionStart),
                        m_html.CreateRow("Execution Stop", data.ExecutionPerformance.ExecutionStop),
                        m_html.CreateRow("WorkerId", data.ExecutionPerformance.WorkerId.ToString(CultureInfo.InvariantCulture)),
                        m_html.CreateEnumRow("ExecutionLevel", data.ExecutionPerformance.ExecutionLevel)));
            }
        }

        public override void PipExecutionStepPerformanceReported(PipExecutionStepPerformanceEventData data)
        {
            if (data.PipId == m_pip.PipId)
            {
                m_sections.Add(
                    m_html.CreateBlock(
                        "Pip Execution Step Performance Event Data",
                        m_html.CreateRow("StartTime", data.StartTime),
                        m_html.CreateRow("Duration", data.Duration),
                        m_html.CreateEnumRow("Step", data.Step),
                        m_html.CreateEnumRow("Dispatcher", data.Dispatcher)));
            }
        }

        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            if (data.PipId == m_pip.PipId)
            {
                m_sections.Add(
                    m_html.CreateBlock(
                        "Process Execution Monitoring",
                        m_html.CreateRow("ReportedProcesses", new XElement("div", data.ReportedProcesses.Select(RenderReportedProcess))),
                        m_html.CreateRow("ReportedFileAcceses", new XElement("div", data.ReportedFileAccesses.Select(RenderReportedFileAccess))),
                        m_html.CreateRow("WhitelistedReportedFileAccesses", new XElement("div", data.WhitelistedReportedFileAccesses.Select(RenderReportedFileAccess))),
                        m_html.CreateRow("ProcessDetouringStatuses", new XElement("div", data.ProcessDetouringStatuses.Select(RenderProcessDetouringStatusData)))));
            }
        }

        private XElement RenderReportedFileAccess(ReportedFileAccess data)
        {
            return new XElement(
                "div",
                new XAttribute("class", "miniGroup"),
                m_html.CreateRow("Path", data.Path),
                m_html.CreateEnumRow("CreationDisposition", data.CreationDisposition),
                m_html.CreateEnumRow("DesiredAccess", data.DesiredAccess),
                m_html.CreateEnumRow("ShareMode", data.ShareMode),
                m_html.CreateEnumRow("Status", data.Status),
                m_html.CreateEnumRow("RequestedAccess", data.RequestedAccess),
                m_html.CreateEnumRow("Operation", data.Operation),
                m_html.CreateEnumRow("FlagsAndAttributes", data.FlagsAndAttributes),
                m_html.CreateRow("Error", data.Error.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("Usn", data.Usn.Value.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("ManifestPath", data.ManifestPath),
                m_html.CreateRow("Process", data.Process.ProcessId.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("ExplicitlyReported", data.ExplicitlyReported),
                m_html.CreateRow("EnumeratePattern", data.EnumeratePattern));
        }

        private XElement RenderReportedProcess(ReportedProcess data)
        {
            return new XElement(
                "div",
                new XAttribute("class", "miniGroup"),
                m_html.CreateRow("ProcessId", data.ProcessId.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("ParentProcessId", data.ParentProcessId.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("Path", data.Path),
                m_html.CreateRow("ProcessArgs", data.ProcessArgs),
                m_html.CreateRow("CreationTime", data.CreationTime),
                m_html.CreateRow("ExitTime", data.ExitTime),
                m_html.CreateRow("ExitCode", data.ExitCode.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("KernelTime", data.KernelTime),
                m_html.CreateRow("UserTime", data.UserTime),
                m_html.CreateRow("IOCounters.Read", PrintIoTypeCounters(data.IOCounters.ReadCounters)),
                m_html.CreateRow("IOCounters.Write", PrintIoTypeCounters(data.IOCounters.WriteCounters)),
                m_html.CreateRow("IOCounters.Other", PrintIoTypeCounters(data.IOCounters.OtherCounters)));
        }

        private XElement RenderProcessDetouringStatusData(ProcessDetouringStatusData data)
        {
            return new XElement(
                "div",
                new XAttribute("class", "miniGroup"),
                m_html.CreateRow("ProcessId", data.ProcessId.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("JobId", data.Job.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("ReportStatus", data.ReportStatus.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("ProcessName", data.ProcessName),
                m_html.CreateRow("StartApplicationName", data.StartApplicationName),
                m_html.CreateRow("StartCommandLine", data.StartCommandLine),
                m_html.CreateRow("NeedsInjection", data.NeedsInjection),
                m_html.CreateRow("DisableDetours", data.DisableDetours),
                m_html.CreateRow("CreationFlags", data.CreationFlags.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("Detoured", data.Detoured),
                m_html.CreateRow("Error", data.Error.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("CreateProcessStatusReturn", data.CreateProcessStatusReturn.ToString(CultureInfo.InvariantCulture)));
        }

        private string PrintIoTypeCounters(IOTypeCounters counters)
        {
            return $"opCount: {counters.OperationCount}, transferCount: {counters.TransferCount}";
        }

        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            if (data.PipId == m_pip.PipId)
            {
                m_sections.Add(
                    m_html.CreateBlock(
                        "Process Fingerprint Computed",
                        m_html.CreateEnumRow("Kind", data.Kind),
                        m_html.CreateRow("WeakContentFingerprintHash", data.WeakFingerprint.Hash.ToHex()),
                        m_html.CreateRow("StrongFingerprintComputations", "TODO")));
            }
        }

        public override void ObservedInputs(ObservedInputsEventData data)
        {
            if (data.PipId == m_pip.PipId)
            {
                m_sections.Add(
                    m_html.CreateBlock(
                        "Observed Inputs",
                        m_html.CreateRow(
                            "Observed Inputs",
                            new XElement(
                                "div",
                                data.ObservedInputs.Select(
                                    oi =>
                                        new XElement(
                                            "div",
                                            new XAttribute("class", "miniGroup"),
                                            m_html.CreateRow("Path", oi.Path),
                                            m_html.CreateEnumRow("Type", oi.Type),
                                            m_html.CreateRow("Hash", oi.Hash.ToHex()),
                                            m_html.CreateRow("IsSearchpath", oi.IsSearchPath),
                                            m_html.CreateRow("IsDirectoryPath", oi.IsDirectoryPath),
                                            m_html.CreateRow("DirectoryEnumeration", oi.DirectoryEnumeration)))))));
            }
        }

        public override void DependencyViolationReported(DependencyViolationEventData data)
        {
            if (data.ViolatorPipId == m_pip.PipId || data.RelatedPipId == m_pip.PipId)
            {
                m_sections.Add(
                    m_html.CreateBlock(
                        "Dependecy Violation",
                        m_html.CreateRow("Violator", data.ViolatorPipId),
                        m_html.CreateRow("Related", data.RelatedPipId),
                        m_html.CreateEnumRow("ViolationType", data.ViolationType),
                        m_html.CreateEnumRow("AccessLevel", data.AccessLevel),
                        m_html.CreateRow("Path", data.Path)));
            }
        }

        public override void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
        {
            if (data.PipId == m_pip.PipId)
            {
                m_sections.Add(
                    m_html.CreateBlock(
                        "Directory Membership Hashed",
                        m_html.CreateRow("Directory", data.Directory),
                        m_html.CreateRow("IsSearchPath", data.IsSearchPath),
                        m_html.CreateRow("IsStatic", data.IsStatic),
                        m_html.CreateRow("EnumeratePatternRegex", data.EnumeratePatternRegex),
                        m_html.CreateRow("Members", data.Members)));
            }
        }

        public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        {
            foreach (var item in data.DirectoryOutputs)
            {
                m_directoryContents[item.directoryArtifact] = item.fileArtifactArray;
            }
        }

        private XElement GetPipSpecificDetails(Pip pip)
        {
            switch (pip.PipType)
            {
                case PipType.CopyFile:
                    return GetCopyFileDetails((CopyFile)pip);
                case PipType.Process:
                    return GetProcessDetails((Process)pip);
                case PipType.Ipc:
                    return GetIpcPipDetails((IpcPip)pip);
                case PipType.Value:
                    return GetValuePipDetails((ValuePip)pip);
                case PipType.SpecFile:
                    return GetSpecFilePipDetails((SpecFilePip)pip);
                case PipType.Module:
                    return GetModulePipDetails((ModulePip)pip);
                case PipType.HashSourceFile:
                    return GetHashSourceFileDetails((HashSourceFile)pip);
                case PipType.SealDirectory:
                    return GetSealDirectoryDetails((SealDirectory)pip);
                case PipType.WriteFile:
                    return GetWriteFileDetails((WriteFile)pip);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private XElement GetWriteFileDetails(WriteFile pip)
        {
            return m_html.CreateBlock(
                "WriteFile Pip Details",
                m_html.CreateRow("Contents", pip.Contents),
                m_html.CreateRow("File Encoding", pip.Encoding.ToString()));
        }

        private XElement GetCopyFileDetails(CopyFile pip)
        {
            return m_html.CreateBlock(
                "CopyFile Pip Details",
                m_html.CreateRow("Source", pip.Source),
                m_html.CreateRow("Destination", pip.Destination));
        }

        private XElement GetProcessDetails(Process pip)
        {
            return new XElement(
                "div",

                m_html.CreateBlock(
                    "Process Invocation Details",
                    m_html.CreateRow("Executable", pip.Executable),
                    m_html.CreateRow("Tool Description", pip.ToolDescription),
                    m_html.CreateRow("Arguments", pip.Arguments),
                    m_html.CreateRow("ResponseFile Path", pip.ResponseFile),
                    m_html.CreateRow("ResponseFile Contents", pip.ResponseFileData),
                    m_html.CreateRow("Environment Variables", new XElement(
                        "table",
                        pip.EnvironmentVariables.Select(envVar => new XElement(
                            "tr",
                            new XElement("td", envVar.Name.ToString(StringTable)),
                            new XElement("td", envVar.Value.IsValid ? envVar.Value.ToString(PathTable) : "[PassThroughValue]")))))),

                m_html.CreateBlock(
                    "Process in/out handling",
                    m_html.CreateRow("StdIn File", pip.StandardInput.File),
                    m_html.CreateRow("StdIn Data", pip.StandardInput.Data),
                    m_html.CreateRow("StdOut", pip.StandardOutput),
                    m_html.CreateRow("StdErr", pip.StandardError),
                    m_html.CreateRow("Std Directory", pip.StandardDirectory),
                    m_html.CreateRow("Warning RegEx", pip.WarningRegex.Pattern),
                    m_html.CreateRow("Error RegEx", pip.ErrorRegex.Pattern)),

                m_html.CreateBlock(
                    "Process Directories",
                    m_html.CreateRow("Working Directory", pip.WorkingDirectory),
                    m_html.CreateRow("Unique Output Directory", pip.UniqueOutputDirectory),
                    m_html.CreateRow("Temporary Directory", pip.TempDirectory),
                    m_html.CreateRow("Extra Temp Directories", pip.AdditionalTempDirectories)),

                m_html.CreateBlock(
                    "Process Advanced option",
                    m_html.CreateRow("Timeout (warning)", pip.WarningTimeout?.ToString()),
                    m_html.CreateRow("Timeout (error)", pip.Timeout?.ToString()),
                    m_html.CreateRow("Success Codes", pip.SuccessExitCodes.Select(code => code.ToString(CultureInfo.InvariantCulture))),
                    m_html.CreateRow("Semaphores", pip.Semaphores.Select(CreateSemaphore)),
                    m_html.CreateRow("HasUntrackedChildProcesses", pip.HasUntrackedChildProcesses),
                    m_html.CreateRow("ProducesPathIndependentOutputs", pip.ProducesPathIndependentOutputs),
                    m_html.CreateRow("OutputsMustRemainWritable", pip.OutputsMustRemainWritable),
                    m_html.CreateRow("AllowPreserveOutputs", pip.AllowPreserveOutputs)),

                m_html.CreateBlock(
                    "Process inputs/outputs",
                    m_html.CreateRow("File Dependencies", pip.Dependencies),
                    m_html.CreateRow("Directory Dependencies", GetDirectoryDependencies(pip.DirectoryDependencies), sortEntries: false),
                    m_html.CreateRow("Pip Dependencies", pip.OrderDependencies),
                    m_html.CreateRow("File Outputs", pip.FileOutputs.Select(output => output.Path.ToString(PathTable) + " (" + Enum.Format(typeof(FileExistence), output.FileExistence, "f") + ")")),
                    m_html.CreateRow("Directory Outputs", GetDirectoryOutputsWithContent(pip), sortEntries: false),
                    m_html.CreateRow("Untracked Paths", pip.UntrackedPaths),
                    m_html.CreateRow("Untracked Scopes", pip.UntrackedScopes)),

                m_html.CreateBlock(
                    "Service details",
                    m_html.CreateRow("Is Service ", pip.IsService),
                    m_html.CreateRow("ShutdownProcessPipId", pip.ShutdownProcessPipId),
                    m_html.CreateRow("ServicePipDependencies", pip.ServicePipDependencies),
                    m_html.CreateRow("IsStartOrShutdownKind", pip.IsStartOrShutdownKind)));
        }

        private string CreateSemaphore(ProcessSemaphoreInfo semaphore)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} (value:{1} limit:{2})",
                semaphore.Name.ToString(StringTable),
                semaphore.Value,
                semaphore.Limit);
        }

        private XElement GetIpcPipDetails(IpcPip pip)
        {
            return m_html.CreateBlock(
                "IpcPip Details",
                m_html.CreateRow("Ipc MonikerInfo", pip.IpcInfo.IpcMonikerId),
                m_html.CreateRow("MessageBody", pip.MessageBody),
                m_html.CreateRow("OutputFile", pip.OutputFile),
                m_html.CreateRow("ServicePip Dependencies", pip.ServicePipDependencies),
                m_html.CreateRow("File Dependencies", pip.FileDependencies),
                m_html.CreateRow("Directory Dependencies", pip.DirectoryDependencies),
                m_html.CreateRow("LazilyMaterialized File Dependencies", pip.LazilyMaterializedDependencies.Where(a => a.IsFile).Select(a => a.FileArtifact)),
                m_html.CreateRow("LazilyMaterialized Directory Dependencies", pip.LazilyMaterializedDependencies.Where(a => a.IsDirectory).Select(a => a.DirectoryArtifact)),
                m_html.CreateRow("IsServiceFinalization", pip.IsServiceFinalization),
                m_html.CreateRow("MustRunOnMaster", pip.MustRunOnMaster));
        }

        private XElement GetValuePipDetails(ValuePip pip)
        {
            return m_html.CreateBlock(
                "ValuePip Details",
                m_html.CreateRow("Symbol", pip.Symbol),
                m_html.CreateRow("Qualifier", pip.Qualifier.ToString()),
                m_html.CreateRow("SpecFile", pip.LocationData.Path),
                m_html.CreateRow("Location", pip.LocationData));
        }

        private XElement GetSpecFilePipDetails(SpecFilePip pip)
        {
            return m_html.CreateBlock(
                "SpecFilePip Details",
                m_html.CreateRow("SpecFile", pip.SpecFile),
                m_html.CreateRow("Definition File", pip.DefinitionLocation.Path),
                m_html.CreateRow("Definition ", pip.DefinitionLocation),
                m_html.CreateRow("Module", GetModuleName(pip.OwningModule)));
        }

        private XElement GetModulePipDetails(ModulePip pip)
        {
            return m_html.CreateBlock(
                "ModulePip Details",
                m_html.CreateRow("Identity", pip.Identity),
                m_html.CreateRow("Definition File", pip.Location.Path),
                m_html.CreateRow("Definition ", pip.Location));
        }

        private XElement GetHashSourceFileDetails(HashSourceFile pip)
        {
            return m_html.CreateBlock(
                "HashSourceFile Pip Details",
                m_html.CreateRow("Artifact", pip.Artifact));
        }

        private XElement GetSealDirectoryDetails(SealDirectory pip)
        {
            return m_html.CreateBlock(
                "SealDirectory Pip Details",
                m_html.CreateEnumRow("Kind", pip.Kind),
                m_html.CreateRow("Scrub", pip.Scrub),
                m_html.CreateRow("DirectoryRoot", pip.Directory),
                m_html.CreateRow("Contents", pip.Contents));
        }

        private string GetModuleName(ModuleId value)
        {
            return value.IsValid ? m_moduleIdToFriendlyName[value] : null;
        }

        private List<string> GetDirectoryOutputsWithContent(Process pip)
        {
            var outputs = new List<string>();
            var rootExpander = new RootExpander(PathTable);

            foreach (var directoryOutput in pip.DirectoryOutputs)
            {
                outputs.Add(FormattableStringEx.I($"{directoryOutput.Path.ToString(PathTable)} (PartialSealId: {directoryOutput.PartialSealId}, IsSharedOpaque: {directoryOutput.IsSharedOpaque})"));
                if (m_directoryContents.TryGetValue(directoryOutput, out var directoryContent))
                {
                    foreach (var file in directoryContent)
                    {
                        outputs.Add(FormattableStringEx.I($"|--- {file.Path.ToString(PathTable, rootExpander)}"));
                    }
                }
            }

            return outputs;
        }

        /// <summary>
        /// Returns a properly formatted/sorted list of directory dependencies.
        /// </summary>
        private List<string> GetDirectoryDependencies(ReadOnlyArray<DirectoryArtifact> dependencies)
        {
            var result = new List<string>();
            var directories = new Stack<(DirectoryArtifact artifact, string path, int tabCount)>(
                dependencies
                    .Select(d => (artifact: d, path: d.Path.ToString(PathTable), 0))
                    .OrderByDescending(tupple => tupple.path));

            while (directories.Count > 0)
            {
                var directory = directories.Pop();
                result.Add(directory.tabCount == 0
                    ? FormattableStringEx.I($"{directory.path} (PartialSealId: {directory.artifact.PartialSealId}, IsSharedOpaque: {directory.artifact.IsSharedOpaque})")
                    : FormattableStringEx.I($"|{string.Concat(Enumerable.Repeat("---", directory.tabCount))}{directory.path} (PartialSealId: {directory.artifact.PartialSealId}, IsSharedOpaque: {directory.artifact.IsSharedOpaque})"));

                var sealPipId = CachedGraph.PipGraph.GetSealedDirectoryNode(directory.artifact).ToPipId();

                if (PipTable.IsSealDirectoryComposite(sealPipId))
                {
                    var sealPip = (SealDirectory)CachedGraph.PipGraph.GetSealedDirectoryPip(directory.artifact, PipQueryContext.SchedulerExecuteSealDirectoryPip);
                    foreach (var nestedDirectory in sealPip.ComposedDirectories.Select(d => (artifact: d, path: d.Path.ToString(PathTable))).OrderByDescending(tupple => tupple.path))
                    {
                        directories.Push((nestedDirectory.artifact, nestedDirectory.path, directory.tabCount + 1));
                    }
                }
            }

            return result;
        }
    }
}
