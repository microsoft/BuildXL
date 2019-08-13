// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Linq;
using BuildXL.Analyzers.Core.XLGPlusPlus;
using BuildXL.ToolSupport;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeNewDumpPipAnalyzer()
        {
            string outputFilePath = null;
            string inputDirPath = null;
            long semiStableHash = 0;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals("inputDir", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("i", StringComparison.OrdinalIgnoreCase))
                {
                    inputDirPath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals("pip", StringComparison.OrdinalIgnoreCase) ||
                         opt.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    semiStableHash = ParseSemistableHash(opt);
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

            if (string.IsNullOrEmpty(inputDirPath))
            {
                throw Error("/inputDir parameter is required");
            }

            if (semiStableHash == 0)
            {
                throw Error("/pip parameter is required");
            }

            return new NewDumpPipAnalyzer(GetAnalysisInput(), outputFilePath, inputDirPath, semiStableHash);
        }

        private static void WriteNewDumpPipAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("New Dump Pip Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.NewDumpPip), "Generates an html file containing information about the requested pip, using the RocksDB database as the source");
            writer.WriteOption("inputDir", "Required. The directory to read the RocksDB database from", shortName: "i");
            writer.WriteOption("outputFile", "Required. The location of the output file for critical path analysis.", shortName: "o");
            writer.WriteOption("pip", "Required. The formatted semistable hash of a pip to dump (must start with 'Pip', e.g., 'PipC623BCE303738C69')");
        }
    }

    /// <summary>
    /// Exports a JSON structured graph, including per-pip static and execution details.
    /// </summary>
    public sealed class NewDumpPipAnalyzer : Analyzer
    {
        private readonly string m_outputFilePath;
        private readonly string m_inputDirPath;
        private readonly long m_semiStableHash;
        private readonly Stopwatch m_stopWatch;

        //private readonly Dictionary<ModuleId, string> m_moduleIdToFriendlyName = new Dictionary<ModuleId, string>();
        //private readonly ConcurrentBigMap<DirectoryArtifact, IReadOnlyList<FileArtifact>> m_directoryContents = new ConcurrentBigMap<DirectoryArtifact, IReadOnlyList<FileArtifact>>();

        public NewDumpPipAnalyzer(AnalysisInput input, string outputFilePath, string inputDirPath, long semiStableHash)
            : base(input)
        {
            m_outputFilePath = outputFilePath;
            m_inputDirPath = inputDirPath;
            m_semiStableHash = semiStableHash;
            m_stopWatch = new Stopwatch();
            m_stopWatch.Start();
        }

        /// <inheritdoc/>
        protected override bool ReadEvents()
        {
            // Do nothing. This analyzer does not read events.
            return true;
        }


        public override int Analyze()
        {
            using (var dataStore = new XldbDataStore(storeDirectory: m_inputDirPath))
            {
                //dataStore.GetPipsOfType(Xldb.PipType.CopyFile).ToList().ForEach(p => Console.WriteLine(p.ToString()));

                var pip = dataStore.GetPipBySemiStableHash(m_semiStableHash, out var pipType);
                if (pip == null)
                {
                    Console.WriteLine($"Pip with the SemiStableHash {m_semiStableHash} was not found. Exiting Analyzer");
                    return 1;
                }

                dynamic testVar = null;

                switch (pipType)
                {
                    case Xldb.PipType.CopyFile:
                        testVar = (Xldb.CopyFile)pip;
                        break;
                    case Xldb.PipType.Module:
                        testVar = (Xldb.ModulePip)pip;
                        break;
                    case Xldb.PipType.SealDirectory:
                        testVar = (Xldb.SealDirectory)pip;
                        break;
                    case Xldb.PipType.WriteFile:
                        testVar = (Xldb.WriteFile)pip;
                        break;
                    case Xldb.PipType.Process:
                        testVar = (Xldb.ProcessPip)pip;
                        break;
                    case Xldb.PipType.HashSourceFile:
                        testVar = (Xldb.HashSourceFile)pip;
                        break;
                    case Xldb.PipType.Ipc:
                        testVar = (Xldb.IpcPip)pip;
                        break;
                    case Xldb.PipType.SpecFile:
                        testVar = (Xldb.SpecFilePip)pip;
                        break;
                }

                Console.WriteLine(pipType.ToString());
                Console.WriteLine(pip.ToString());

                dataStore.GetBXLInvocationEvents().ToList().ForEach(ev => Console.WriteLine(ev.ToString()));

                Console.WriteLine(dataStore.GetEventByKey(Xldb.ExecutionEventId.PipExecutionPerformance, ((Xldb.CopyFile)pip).ParentPipInfo.PipId)?.ToString() ?? "PipExecutionPerformance empty or null");
                Console.WriteLine(dataStore.GetEventByKey(Xldb.ExecutionEventId.PipExecutionStepPerformanceReported, ((Xldb.CopyFile)pip).ParentPipInfo.PipId)?.ToString() ?? "PipExecutionStepPerformanceReported empty or null");
                Console.WriteLine(dataStore.GetEventByKey(Xldb.ExecutionEventId.ProcessExecutionMonitoringReported, ((Xldb.CopyFile)pip).ParentPipInfo.PipId)?.ToString() ?? "ProcessExecutionMonitoringReported empty or null");
                Console.WriteLine(dataStore.GetEventByKey(Xldb.ExecutionEventId.ProcessFingerprintComputation, ((Xldb.CopyFile)pip).ParentPipInfo.PipId)?.ToString() ?? "ProcessFingerprintComputation empty or null");
                Console.WriteLine(dataStore.GetEventByKey(Xldb.ExecutionEventId.ObservedInputs, ((Xldb.CopyFile)pip).ParentPipInfo.PipId)?.ToString() ?? "ObservedInputs empty or null");
                Console.WriteLine(dataStore.GetEventByKey(Xldb.ExecutionEventId.DirectoryMembershipHashed, ((Xldb.CopyFile)pip).ParentPipInfo.PipId)?.ToString() ?? "DirectoryMembershipHashed empty or null");

                // TODO: -> don't put those in the key?? what if u skip one of them since it is an || ... instead provide an API specifically for those 2 fields (iterating over all the objects)
                Console.WriteLine(dataStore.GetEventByKey(Xldb.ExecutionEventId.DependencyViolationReported, ((Xldb.CopyFile)pip).ParentPipInfo.PipId)?.ToString() ?? "DependencyViolationReported empty or null");

                /// The things below all require pipGraph
                // Step 12: We want all possible directories and outputs and then we will go over just our directories and see if it in there

                // Step 13: Directory Dependencies (this will require the pip graph)

                // Step 14: Need pip dependencies (immediate source and non-source) ... so will need pip graph for this as well
            }
            Console.WriteLine("\n\nTotal time for writing {0} seconds", m_stopWatch.ElapsedMilliseconds / 1000.0);
            return 0;
        }

        public void PrintMessages<T>(T msg)
        {
            Console.WriteLine(msg.ToString());
        }

        //public override void DependencyViolationReported(DependencyViolationEventData data)
        //{
        //    if (data.ViolatorPipId == m_pip.PipId || data.RelatedPipId == m_pip.PipId)
        //    {
        //        m_sections.Add(
        //            m_html.CreateBlock(
        //                "Dependecy Violation",
        //                m_html.CreateRow("Violator", data.ViolatorPipId),
        //                m_html.CreateRow("Related", data.RelatedPipId),
        //                m_html.CreateEnumRow("ViolationType", data.ViolationType),
        //                m_html.CreateEnumRow("AccessLevel", data.AccessLevel),
        //                m_html.CreateRow("Path", data.Path)));
        //    }
        //}

        //public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        //{
        //    foreach (var item in data.DirectoryOutputs)
        //    {
        //        m_directoryContents[item.directoryArtifact] = item.fileArtifactArray;
        //    }
        //}

        //private string GetModuleName(ModuleId value)
        //{
        //    return value.IsValid ? m_moduleIdToFriendlyName[value] : null;
        //}

        //private List<string> GetDirectoryOutputsWithContent(Process pip)
        //{
        //    var outputs = new List<string>();
        //    var rootExpander = new RootExpander(PathTable);

        //    foreach (var directoryOutput in pip.DirectoryOutputs)
        //    {
        //        outputs.Add(FormattableStringEx.I($"{directoryOutput.Path.ToString(PathTable)} (PartialSealId: {directoryOutput.PartialSealId}, IsSharedOpaque: {directoryOutput.IsSharedOpaque})"));
        //        if (m_directoryContents.TryGetValue(directoryOutput, out var directoryContent))
        //        {
        //            foreach (var file in directoryContent)
        //            {
        //                outputs.Add(FormattableStringEx.I($"|--- {file.Path.ToString(PathTable, rootExpander)}"));
        //            }
        //        }
        //    }

        //    return outputs;
        //}

        ///// <summary>
        ///// Returns a properly formatted/sorted list of directory dependencies.
        ///// </summary>
        //private List<string> GetDirectoryDependencies(ReadOnlyArray<DirectoryArtifact> dependencies)
        //{
        //    var result = new List<string>();
        //    var directories = new Stack<(DirectoryArtifact artifact, string path, int tabCount)>(
        //        dependencies
        //            .Select(d => (artifact: d, path: d.Path.ToString(PathTable), 0))
        //            .OrderByDescending(tupple => tupple.path));

        //    while (directories.Count > 0)
        //    {
        //        var directory = directories.Pop();
        //        result.Add(directory.tabCount == 0
        //            ? FormattableStringEx.I($"{directory.path} (PartialSealId: {directory.artifact.PartialSealId}, IsSharedOpaque: {directory.artifact.IsSharedOpaque})")
        //            : FormattableStringEx.I($"|{string.Concat(Enumerable.Repeat("---", directory.tabCount))}{directory.path} (PartialSealId: {directory.artifact.PartialSealId}, IsSharedOpaque: {directory.artifact.IsSharedOpaque})"));

        //        var sealPipId = CachedGraph.PipGraph.GetSealedDirectoryNode(directory.artifact).ToPipId();

        //        if (PipTable.IsSealDirectoryComposite(sealPipId))
        //        {
        //            var sealPip = (SealDirectory)CachedGraph.PipGraph.GetSealedDirectoryPip(directory.artifact, PipQueryContext.SchedulerExecuteSealDirectoryPip);
        //            foreach (var nestedDirectory in sealPip.ComposedDirectories.Select(d => (artifact: d, path: d.Path.ToString(PathTable))).OrderByDescending(tupple => tupple.path))
        //            {
        //                directories.Push((nestedDirectory.artifact, nestedDirectory.path, directory.tabCount + 1));
        //            }
        //        }
        //    }

        //    return result;
        //}
    }
}
