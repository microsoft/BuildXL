// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeScheduledInputsOutputsAnalyzer()
        {
            string outputFile = null;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFile = ParseSingletonPathOption(opt, outputFile);
                }
                else
                {
                    throw Error("Unknown option for scheduled input/output analyzer: {0}", opt.Name);
                }
            }

            if (outputFile == null)
            {
                throw Error("'outputFile' parameter is required for scheduled input/output analyzer");
            }

            outputFile = Path.GetFullPath(outputFile);

            return new ScheduledInputsOutputsAnalyzer(GetAnalysisInput())
            {
                OutputFile = outputFile
            };
        }
    }

    /// <summary>
    /// Analyzer used to generate a log file with all process pips, with inputs and outputs for each one
    /// </summary>
    internal sealed class ScheduledInputsOutputsAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the log file
        /// </summary>
        public string OutputFile;

        private readonly Dictionary<PipId, PipExecutionDirectoryOutputs> m_directoryOutputContent;
        private readonly ConcurrentBigMap<PipId, List<AbsolutePath>> m_observedInputs = new ConcurrentBigMap<PipId, List<AbsolutePath>>();

        public ScheduledInputsOutputsAnalyzer(AnalysisInput input)
            : base(input)
        {
            m_directoryOutputContent = new Dictionary<PipId, PipExecutionDirectoryOutputs>();
        }

        public override int Analyze()
        {
            var allProcessPips = CachedGraph.PipGraph.RetrievePipReferencesOfType(PipType.Process)
                .Select(lazyPip => (Process)lazyPip.HydratePip())
                .ToList();

            using (var outputStream = File.Create(OutputFile, bufferSize: 64 << 10 /* 64 KB */))
            using (var writer = new StreamWriter(outputStream))
            {
                writer.WriteLine($"Total number of process pips: {allProcessPips.Count}");
                foreach (var pip in allProcessPips)
                {
                    AnalyzePip(pip, writer);
                }
            }

            return 0;
        }

        public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        {
            // Just collect all the directory output data for each reported pip
            m_directoryOutputContent[data.PipId] = data;
        }

        public override void ObservedInputs(ObservedInputsEventData data)
        {
            m_observedInputs[data.PipId] = new List<AbsolutePath>(data.ObservedInputs.Where(oi => !oi.IsDirectoryPath).Select(oi => oi.Path).Distinct());
        }

        private void AnalyzePip(Process pip, StreamWriter writer)
        {
            writer.WriteLine($"[{pip.GetDescription(PipGraph.Context)}]");
            writer.WriteLine($"\tExecutable: {pip.Executable.Path.ToString(PathTable)}");
            writer.WriteLine($"\tArguments: {pip.Arguments.ToString(PathTable)}");
            LogPathCollection("File inputs:", pip.Dependencies.Select(input => input.Path).ToList(), writer);
            ReportDirectoryInputs(pip, writer);
            LogPathCollection("File outputs:", pip.FileOutputs.Select(output => output.Path).ToList(), writer);
            ReportDirectoryOutputs(pip, writer);
            LogPathCollection("Untracked artifacts:", pip.UntrackedPaths.Union(pip.UntrackedScopes).ToList(), writer);
        }

        private void ReportDirectoryOutputs(Process pip, StreamWriter writer)
        {
            // All the declared output directory
            HashSet<AbsolutePath> declaredDirectoryOutputs = pip.DirectoryOutputs.Select(input => input.Path).ToHashSet();

            // All the directory outputs with runtime content. This depends on the availability of the execution log to be present
            var directoryOutputsWithRuntimeContent = !m_directoryOutputContent.ContainsKey(pip.PipId)
                ? ReadOnlyArray<(DirectoryArtifact directoryArtifact, ReadOnlyArray<FileArtifact> fileArtifactArray)>.Empty
                : m_directoryOutputContent[pip.PipId].DirectoryOutputs;

            if (declaredDirectoryOutputs.Count > 0)
            {
                writer.WriteLine("\tDirectory outputs:");

                // Print out all the directory outputs with available runtime content first
                foreach (var directoryOutputWithContent in directoryOutputsWithRuntimeContent)
                {
                    var directoryArtifact = directoryOutputWithContent.directoryArtifact;
                    var content = directoryOutputWithContent.fileArtifactArray;

                    writer.WriteLine($"\t\t{directoryArtifact.Path.ToString(PathTable)}");
                    writer.WriteLine($"\t\tContent:");
                    foreach (var fileArtifact in content)
                    {
                        writer.WriteLine($"\t\t\t{fileArtifact.Path.ToString(PathTable)}");
                    }

                    // If the directory has runtime content, we don't want to report it again
                    declaredDirectoryOutputs.Remove(directoryArtifact.Path);
                }

                // Now print out the ones where the content is not available
                foreach (var declaredDirectoryOutput in declaredDirectoryOutputs)
                {
                    writer.WriteLine($"\t\t{declaredDirectoryOutput.ToString(PathTable)}");
                }
            }
        }

        private void ReportDirectoryInputs(Process pip, StreamWriter writer)
        {
            var inputs = m_observedInputs.ContainsKey(pip.PipId) ? m_observedInputs[pip.PipId] : new List<AbsolutePath>();
            var directories = pip.DirectoryDependencies.Select(directory => directory.Path).ToList();

            if (directories.Count > 0)
            {
                writer.WriteLine("\tDirectory inputs:");

                foreach (var directory in directories)
                {
                    bool headerPrinted = false;

                    writer.WriteLine($"\t\t{directory.ToString(PathTable)}");

                    foreach (var input in inputs)
                    {
                        if (input.IsWithin(PathTable, directory))
                        {
                            if (!headerPrinted)
                            {
                                writer.WriteLine($"\t\tAccessed content:");
                                headerPrinted = true;
                            }
                            writer.WriteLine($"\t\t\t{input.ToString(PathTable)}");
                        }
                    }
                }
            }
        }

        private void LogPathCollection(string title, ICollection<AbsolutePath> paths, StreamWriter writer)
        {
            if (paths.Count > 0)
            {
                writer.WriteLine($"\t{title}");

                foreach(var path in paths)
                {
                    writer.WriteLine($"\t\t{path.ToString(PathTable)}");
                }
            }
        }
    }
}
