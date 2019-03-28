// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Processes;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeToolEnumerationAnalyzer()
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

            return new ToolEnumerationAnalyzer(GetAnalysisInput())
            {
                OutputFilePath = outputFilePath,
            };
        }

        private static void WriteToolEnumerationHelp(HelpWriter writer)
        {
            writer.WriteBanner("Observed Tool Enumeration Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.ToolEnumeration), "Generates a text file containing enumeration by tools launched by pips. NOTE: Requires build with /logObservedFileAccesses.");
            writer.WriteOption("outputFile", "Required. The file where to write the results", shortName: "o");
        }
    }

    /// <summary>
    /// Analyzer used to dump observed inputs
    /// </summary>
    internal sealed class ToolEnumerationAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the output file
        /// </summary>
        public string OutputFilePath;

        private readonly Dictionary<PipId, IReadOnlyCollection<ReportedFileAccess>> m_toolEnumerationMap
            = new Dictionary<PipId, IReadOnlyCollection<ReportedFileAccess>>();

        private readonly Dictionary<PipId, ReadOnlyArray<ObservedInput>> m_observedInputsMap
            = new Dictionary<PipId, ReadOnlyArray<ObservedInput>>();

        public ToolEnumerationAnalyzer(AnalysisInput input)
            : base(input)
        {
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            HashSet<(CaseInsensitiveString toolPath, AbsolutePath directoryPath)> toolAndEnumerations = new HashSet<(CaseInsensitiveString toolPath, AbsolutePath directoryPath)>();
            HashSet<AbsolutePath> enumeratedDirectories = new HashSet<AbsolutePath>();
            HashSet<string> enumerationTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var observedInputEntry in m_observedInputsMap)
            {
                var pip = observedInputEntry.Key;
                var observedInputs = observedInputEntry.Value;

                if (!m_toolEnumerationMap.ContainsKey(pip))
                {
                    continue;
                }

                var reportedFileAccesses = m_toolEnumerationMap[pip];

                foreach (var observedInput in observedInputs)
                {
                    if (observedInput.Type == ObservedInputType.DirectoryEnumeration)
                    {
                        enumeratedDirectories.Add(observedInput.Path);
                    }
                }

                foreach (var reportedFileAccess in reportedFileAccesses)
                {
                    var path = reportedFileAccess.ManifestPath;
                    if (reportedFileAccess.Path != null)
                    {
                        if (!AbsolutePath.TryCreate(PathTable, reportedFileAccess.Path, out path))
                        {
                            continue;
                        }
                    }

                    if (!enumeratedDirectories.Contains(path))
                    {
                        continue;
                    }

                    enumerationTools.Add(reportedFileAccess.Process.Path);
                    toolAndEnumerations.Add((new CaseInsensitiveString(reportedFileAccess.Process.Path), path));
                }
            }

            var toolEnumerationList = toolAndEnumerations.ToList();
            toolEnumerationList.Sort((v1, v2) =>
            {
                var result = StringComparer.OrdinalIgnoreCase.Compare(v1.toolPath.Value, v2.toolPath.Value);
                if (result != 0)
                {
                    return result;
                }

                return PathTable.ExpandedPathComparer.Compare(v1.Item2, v2.Item2);
            });

            using (var outputStream = File.Create(OutputFilePath, bufferSize: 64 << 10 /* 64 KB */))
            {
                using (var writer = new StreamWriter(outputStream))
                {
                    writer.WriteLine("Directory Enumeration Tools:");
                    foreach (var tool in enumerationTools.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                    {
                        writer.WriteLine(tool);
                    }

                    writer.WriteLine();
                    writer.WriteLine("Enumerations By Tool:");

                    CaseInsensitiveString last = new CaseInsensitiveString(string.Empty);
                    foreach (var toolEnumerationEntry in toolEnumerationList)
                    {
                        var toolPath = toolEnumerationEntry.Item1;
                        var directoryPath = toolEnumerationEntry.Item2;
                        if (!last.Equals(toolPath))
                        {
                            last = toolPath;
                            writer.WriteLine();
                            writer.WriteLine("Tool: {0}", toolPath.Value);
                        }

                        writer.WriteLine("  Directory: {0}", directoryPath.ToString(PathTable));
                    }

                    toolEnumerationList.Sort((v1, v2) =>
                    {
                        var result = PathTable.ExpandedPathComparer.Compare(v1.Item2, v2.Item2);
                        if (result != 0)
                        {
                            return result;
                        }

                        return StringComparer.OrdinalIgnoreCase.Compare(v1.Item1.Value, v2.Item1.Value);
                    });

                    writer.WriteLine();
                    writer.WriteLine("Enumerations By Directory:");

                    AbsolutePath lastDirectory = AbsolutePath.Invalid;
                    foreach (var toolEnumerationEntry in toolEnumerationList)
                    {
                        var toolPath = toolEnumerationEntry.toolPath;
                        var directoryPath = toolEnumerationEntry.directoryPath;
                        if (!lastDirectory.Equals(directoryPath))
                        {
                            lastDirectory = directoryPath;
                            writer.WriteLine();
                            writer.WriteLine("Directory: {0}", directoryPath.ToString(PathTable));
                        }

                        writer.WriteLine("  Tool: {0}", toolPath.Value);
                    }
                }
            }

            return 0;
        }

        private readonly struct CaseInsensitiveString : IEquatable<CaseInsensitiveString>
        {
            public readonly string Value;

            public CaseInsensitiveString(string value)
            {
                Value = value;
            }

            public override bool Equals(object obj)
            {
                return StructUtilities.Equals(this, obj);
            }

            public override int GetHashCode()
            {
                return StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
            }

            public bool Equals(CaseInsensitiveString other)
            {
                return StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);
            }

            public override string ToString()
            {
                return Value;
            }
        }

        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            m_toolEnumerationMap[data.PipId] = data.ReportedFileAccesses;
        }

        /// <inheritdoc />
        public override void ObservedInputs(ObservedInputsEventData data)
        {
            // Observed inputs are processed twice: once for the cache lookup and once when the process is run. If the strong
            // fingerprint misses, it is possible for the same pip to log input assertions twice. They will be the same
            // so we can just pick the last one
            m_observedInputsMap[data.PipId] = data.ObservedInputs;
        }
    }
}
