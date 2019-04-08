// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeObservedInputSummaryResult()
        {
            string outputFilePath = null;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else
                {
                    throw Error("Unknown option for fingerprint text analysis: {0}", opt.Name);
                }
            }

            return new ObservedInputSummaryAnalyzer(GetAnalysisInput())
            {
                OutputFilePath = outputFilePath,
            };
        }

        private static void WriteObservedInputSummaryHelp(HelpWriter writer)
        {
            writer.WriteBanner("Observed Input Summary Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.ObservedInputSummary), "Generates a text file containing summary level information about ObservedInputs");
            writer.WriteOption("outputFile", "Required. The file where to write the results", shortName: "o");
        }
    }

    /// <summary>
    /// Analyzer used to dump observed inputs
    /// </summary>
    internal sealed class ObservedInputSummaryAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the output file
        /// </summary>
        public string OutputFilePath;

        private readonly Dictionary<PipId, List<ReadOnlyArray<ObservedInput>>> m_observedInputsMap = new Dictionary<PipId, List<ReadOnlyArray<ObservedInput>>>();

        public ObservedInputSummaryAnalyzer(AnalysisInput input)
            : base(input)
        {
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            int absentPathCount = 0;
            int directoryEnumerationCount = 0;
            int fileContentReadCount = 0;
            int existingDirectoryCount = 0;

            Dictionary<AbsolutePath, int> paths = new Dictionary<AbsolutePath, int>();
            Dictionary<PathAtom, int> files = new Dictionary<PathAtom, int>();

            using (var outputStream = File.Create(OutputFilePath, bufferSize: 64 << 10 /* 64 KB */))
            {
                using (var writer = new StreamWriter(outputStream))
                {
                    foreach (var observedInputSet in m_observedInputsMap.OrderBy(kvp => PipGraph.GetPipFromPipId(kvp.Key).SemiStableHash))
                    {
                        foreach (var observedInput in observedInputSet.Value)
                        {
                            foreach (var file in observedInput.OrderBy(item => item.Path.ToString(PathTable)))
                            {
                                int pathCount = 0;
                                paths.TryGetValue(file.Path, out pathCount);
                                pathCount++;
                                paths[file.Path] = pathCount;

                                int fileCount = 0;
                                var atom = file.Path.GetName(PipGraph.Context.PathTable);
                                files.TryGetValue(atom, out fileCount);
                                fileCount++;
                                files[atom] = fileCount;

                                switch (file.Type)
                                {
                                    case ObservedInputType.AbsentPathProbe:
                                        absentPathCount++;
                                        break;
                                    case ObservedInputType.DirectoryEnumeration:
                                        directoryEnumerationCount++;
                                        break;
                                    case ObservedInputType.FileContentRead:
                                        fileContentReadCount++;
                                        break;
                                    case ObservedInputType.ExistingDirectoryProbe:
                                        existingDirectoryCount++;
                                        break;
                                    default:
                                        throw new NotImplementedException();
                                }
                            }
                        }
                    }

                    writer.WriteLine("AbsentPathProbe count: " + absentPathCount);
                    writer.WriteLine("DirectoryEnumeration count: " + directoryEnumerationCount);
                    writer.WriteLine("FileContentRead count: " + fileContentReadCount);
                    writer.WriteLine("existingDirectory count: " + existingDirectoryCount);
                    writer.WriteLine();
                    writer.WriteLine();
                    writer.WriteLine("Paths:");
                    foreach (var item in paths.OrderByDescending(kvp => kvp.Value))
                    {
                        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-10}", item.Value) + item.Key.ToString(CachedGraph.Context.PathTable));
                    }

                    writer.WriteLine();
                    writer.WriteLine();

                    writer.WriteLine("Files:");
                    foreach (var item in files.OrderByDescending(kvp => kvp.Value))
                    {
                        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-10}", item.Value) + item.Key.ToString(CachedGraph.Context.PathTable.StringTable));
                    }
                }
            }

            return 0;
        }

        /// <inheritdoc />
        public override void ObservedInputs(ObservedInputsEventData data)
        {
            // Observed inputs are processed twice: once for the cache lookup and once when the process is run. If the strong
            // fingerprint misses, it is possible for the same pip to log input assertions twice. They will be the same
            // so we can just pick the last one
            List<ReadOnlyArray<ObservedInput>> inputs;
            if (!m_observedInputsMap.TryGetValue(data.PipId, out inputs))
            {
                inputs = new List<ReadOnlyArray<ObservedInput>>();
                m_observedInputsMap.Add(data.PipId, inputs);
            }

            inputs.Add(data.ObservedInputs);
        }
    }
}
