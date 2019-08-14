// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BuildXL.Analyzers.Core.XLGPlusPlus;
using BuildXL.Execution.Analyzer.Xldb;
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
                var pip = dataStore.GetPipBySemiStableHash(m_semiStableHash, out var pipType);

                if (pip == null)
                {
                    Console.WriteLine($"Pip with the SemiStableHash {m_semiStableHash} was not found. Exiting Analyzer");
                    return 1;
                }

                dynamic castedPip = null;

                switch (pipType)
                {
                    case PipType.CopyFile:
                        castedPip = (CopyFile)pip;
                        break;
                    case PipType.Module:
                        castedPip = (ModulePip)pip;
                        break;
                    case PipType.SealDirectory:
                        castedPip = (SealDirectory)pip;
                        break;
                    case PipType.WriteFile:
                        castedPip = (WriteFile)pip;
                        break;
                    case PipType.Process:
                        castedPip = (ProcessPip)pip;
                        break;
                    case PipType.HashSourceFile:
                        castedPip = (HashSourceFile)pip;
                        break;
                    case PipType.Ipc:
                        castedPip = (IpcPip)pip;
                        break;
                    case PipType.SpecFile:
                        castedPip = (SpecFilePip)pip;
                        break;
                }

                Console.WriteLine(pipType.ToString());
                Console.WriteLine(pip.ToString());

                dataStore.GetBXLInvocationEvents().ToList().ForEach(ev => Console.WriteLine(ev.ToString()));

                Console.WriteLine(dataStore.GetEventByKey(ExecutionEventId.PipExecutionPerformance, castedPip.ParentPipInfo.PipId)?.ToString() ?? "PipExecutionPerformance empty or null");
                Console.WriteLine(dataStore.GetEventByKey(ExecutionEventId.PipExecutionStepPerformanceReported, castedPip.ParentPipInfo.PipId)?.ToString() ?? "PipExecutionStepPerformanceReported empty or null");
                Console.WriteLine(dataStore.GetEventByKey(ExecutionEventId.ProcessExecutionMonitoringReported, castedPip.ParentPipInfo.PipId)?.ToString() ?? "ProcessExecutionMonitoringReported empty or null");
                Console.WriteLine(dataStore.GetEventByKey(ExecutionEventId.ProcessFingerprintComputation, castedPip.ParentPipInfo.PipId)?.ToString() ?? "ProcessFingerprintComputation empty or null");
                Console.WriteLine(dataStore.GetEventByKey(ExecutionEventId.ObservedInputs, castedPip.ParentPipInfo.PipId)?.ToString() ?? "ObservedInputs empty or null");
                Console.WriteLine(dataStore.GetEventByKey(ExecutionEventId.DirectoryMembershipHashed, castedPip.ParentPipInfo.PipId)?.ToString() ?? "DirectoryMembershipHashed empty or null");

                var depViolatedEvents = dataStore.GetDependencyViolationReportedEvents();

                foreach (var ev in depViolatedEvents)
                {
                    if (ev.ViolatorPipID == castedPip.ParentPipInfo.PipId || ev.RelatedPipID == castedPip.ParentPipInfo.PipId)
                    {
                        Console.WriteLine(ev.ToString());
                    }
                }

                if (pipType == PipType.Process)
                {
                    Console.WriteLine("Getting directory output information for Process Pip");
                    var pipExecutionDirEvents = dataStore.GetPipExecutionDirectoryOutputsEvents();
                    foreach (var ev in pipExecutionDirEvents)
                    {
                        foreach (var dirOutput in ev.DirectoryOutput)
                        {
                            if (castedPip.DirectoryOutputs.Contains(dirOutput.DirectoryArtifact))
                            {
                                dirOutput.FileArtifactArray.ToList().ForEach(file => Console.WriteLine(file.ToString()));
                            }
                        }
                    }

                    Console.WriteLine("Geting directory dependency information for Process Pip");

                    var pipGraph = dataStore.GetPipGraphMetaData();
                    var directories = new Stack<(DirectoryArtifact artifact, string path)>(
                    ((ProcessPip)castedPip).DirectoryDependencies
                        .Select(d => (artifact: d, path: d.Path.Value))
                        .OrderByDescending(tupple => tupple.path));

                    while (directories.Count > 0)
                    {
                        var directory = directories.Pop();

                        foreach (var kvp in pipGraph.AllSealDirectoriesAndProducers)
                        {
                            if (kvp.Artifact == directory.artifact)
                            {
                                var currPipId = kvp.Value;
                                var currPip = dataStore.GetPipByPipId(currPipId, out var currPipType);

                                if (currPipType == PipType.SealDirectory)
                                {
                                    foreach(var nestedDirectory in ((SealDirectory)currPip).ComposedDirectories.Select(d => (artifact: d, path: d.Path.Value)).OrderByDescending(tupple => tupple.path))
                                    {
                                        directories.Push((nestedDirectory.artifact, nestedDirectory.path));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            Console.WriteLine("\n\nTotal time for writing {0} seconds", m_stopWatch.ElapsedMilliseconds / 1000.0);
            return 0;
        }
    }
}
