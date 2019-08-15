// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BuildXL.Analyzers.Core.XLGPlusPlus;
using BuildXL.Execution.Analyzer.Xldb;
using BuildXL.ToolSupport;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeDumpPipXldbAnalyzer()
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

            return new DumpPipXldbAnalyzer(GetAnalysisInput(), outputFilePath, inputDirPath, semiStableHash);
        }

        private static void WriteDumpPipXldbHelp(HelpWriter writer)
        {
            writer.WriteBanner("Dump Pip Xldb Analyzer");
            writer.WriteModeOption(nameof(AnalysisMode.DumpPipXldb), "Generates an html file containing information about the requested pip, using the RocksDB database as the source");
            writer.WriteOption("inputDir", "Required. The directory to read the RocksDB database from", shortName: "i");
            writer.WriteOption("outputFile", "Required. The location of the output file for critical path analysis.", shortName: "o");
            writer.WriteOption("pip", "Required. The formatted semistable hash of a pip to dump (must start with 'Pip', e.g., 'PipC623BCE303738C69')");
        }
    }

    /// <summary>
    /// Exports a JSON structured graph, including per-pip static and execution details.
    /// </summary>
    public sealed class DumpPipXldbAnalyzer : Analyzer
    {
        private readonly string m_outputFilePath;
        private readonly string m_inputDirPath;
        private readonly long m_semiStableHash;
        private readonly Stopwatch m_stopWatch;

        public DumpPipXldbAnalyzer(AnalysisInput input, string outputFilePath, string inputDirPath, long semiStableHash)
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
            using (var outputStream = File.OpenWrite(m_outputFilePath))
            using (var writer = new StreamWriter(outputStream))
            {
                var pip = dataStore.GetPipBySemiStableHash(m_semiStableHash, out var pipType);

                if (pip == null)
                {
                    Console.WriteLine($"Pip with the SemiStableHash {m_semiStableHash} was not found. Exiting Analyzer ...");
                    return 1;
                }
                Console.WriteLine($"Pip with the SemiStableHash {m_semiStableHash} was found. Logging to output file ...");

                dynamic castedPip = null;

                switch (pipType)
                {
                    case PipType.CopyFile:
                        castedPip = (CopyFile)pip;
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
                    case PipType.Ipc:
                        castedPip = (IpcPip)pip;
                        break;
                }

                writer.WriteLine(pipType.ToString());
                writer.WriteLine(pip.ToString());

                dataStore.GetBXLInvocationEvents().ToList().ForEach(ev => writer.WriteLine(ev.ToString()));

                writer.WriteLine(dataStore.GetEventByKey(ExecutionEventId.PipExecutionPerformance, castedPip.GraphInfo.PipId)?.ToString() ?? "PipExecutionPerformance empty or null");
                writer.WriteLine(dataStore.GetEventByKey(ExecutionEventId.PipExecutionStepPerformanceReported, castedPip.GraphInfo.PipId)?.ToString() ?? "PipExecutionStepPerformanceReported empty or null");
                writer.WriteLine(dataStore.GetEventByKey(ExecutionEventId.ProcessExecutionMonitoringReported, castedPip.GraphInfo.PipId)?.ToString() ?? "ProcessExecutionMonitoringReported empty or null");
                writer.WriteLine(dataStore.GetEventByKey(ExecutionEventId.ProcessFingerprintComputation, castedPip.GraphInfo.PipId)?.ToString() ?? "ProcessFingerprintComputation empty or null");
                writer.WriteLine(dataStore.GetEventByKey(ExecutionEventId.ObservedInputs, castedPip.GraphInfo.PipId)?.ToString() ?? "ObservedInputs empty or null");
                writer.WriteLine(dataStore.GetEventByKey(ExecutionEventId.DirectoryMembershipHashed, castedPip.GraphInfo.PipId)?.ToString() ?? "DirectoryMembershipHashed empty or null");

                var depViolatedEvents = dataStore.GetDependencyViolationReportedEvents();

                foreach (var ev in depViolatedEvents)
                {
                    if (ev.ViolatorPipID == castedPip.GraphInfo.PipId || ev.RelatedPipID == castedPip.GraphInfo.PipId)
                    {
                        writer.WriteLine(ev.ToString());
                    }
                }

                if (pipType == PipType.Process)
                {
                    writer.WriteLine("Getting directory output information for Process Pip");
                    var pipExecutionDirEvents = dataStore.GetPipExecutionDirectoryOutputsEvents();
                    foreach (var ev in pipExecutionDirEvents)
                    {
                        foreach (var dirOutput in ev.DirectoryOutput)
                        {
                            if (castedPip.DirectoryOutputs.Contains(dirOutput.DirectoryArtifact))
                            {
                                dirOutput.FileArtifactArray.ToList().ForEach(file => writer.WriteLine(file.ToString()));
                            }
                        }
                    }

                    writer.WriteLine("Geting directory dependency information for Process Pip");

                    var pipGraph = dataStore.GetPipGraphMetaData();
                    var directories = new Stack<(DirectoryArtifact artifact, string path)>(
                    ((ProcessPip)castedPip).DirectoryDependencies
                        .Select(d => (artifact: d, path: d.Path.Value))
                        .OrderByDescending(tupple => tupple.path));

                    while (directories.Count > 0)
                    {
                        var directory = directories.Pop();
                        writer.WriteLine(directory.ToString());

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
