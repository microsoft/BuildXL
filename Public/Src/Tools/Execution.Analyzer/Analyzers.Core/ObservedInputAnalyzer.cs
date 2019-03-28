// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Collections;
using Newtonsoft.Json;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeObservedInputResult()
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

            return new ObservedInputAnalyzer(GetAnalysisInput())
            {
                OutputFilePath = outputFilePath,
            };
        }

        private static void WriteObservedInputHelp(HelpWriter writer)
        {
            writer.WriteBanner("Observed Input Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.ObservedInput), "Generates a text file containing ObservedInput as discovered at build/cache retrieve");
            writer.WriteOption("outputFile", "Required. The file where to write the results", shortName: "o");
        }
    }

    /// <summary>
    /// Analyzer used to dump observed inputs
    /// </summary>
    internal sealed class ObservedInputAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the output file
        /// </summary>
        public string OutputFilePath;

        private readonly Dictionary<PipId, List<ReadOnlyArray<ObservedInput>>> m_observedInputsMap = new Dictionary<PipId, List<ReadOnlyArray<ObservedInput>>>();

        public ObservedInputAnalyzer(AnalysisInput input)
            : base(input)
        {
        }

        private static void WriteJsonProperty<T>(JsonWriter writer, string name, T value)
        {
            writer.WritePropertyName(name);
            writer.WriteValue(value);
        }


        private static void WritePipJson(JsonWriter writer, global::BuildXL.Pips.Operations.Pip pip, global::BuildXL.Utilities.PipExecutionContext pipExecutionContext)
        {
            writer.WriteStartObject();
            WriteJsonProperty(writer, "SemiStableHash", pip.FormattedSemiStableHash);
            WriteJsonProperty(writer, "Provenance", pip.Provenance.Usage.ToString(pipExecutionContext.PathTable));
            WriteJsonProperty(writer, "Type", pip.PipType.ToString());
            WriteJsonProperty(writer, "Description", pip.GetDescription(pipExecutionContext));
            writer.WriteEndObject();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            using (var outputStream = File.Create(OutputFilePath, bufferSize: 64 << 10 /* 64 KB */))
            {
                using (var streamWriter = new StreamWriter(outputStream))
                using (JsonWriter writer = new JsonTextWriter(streamWriter))
                {
                    writer.Formatting = Formatting.Indented;

                    writer.WriteStartArray();
                    foreach (var observedInputSet in m_observedInputsMap.OrderBy(kvp => PipGraph.GetPipFromPipId(kvp.Key).SemiStableHash))
                    {
                        writer.WriteStartObject();

                        writer.WritePropertyName("Pip");
                        WritePipJson(writer, PipGraph.GetPipFromPipId(observedInputSet.Key), CachedGraph.Context);
                        
                        writer.WritePropertyName("ObservedInputHashesByPath");
                        writer.WriteStartObject();
                        foreach (var observedInput in observedInputSet.Value)
                        {
                            foreach (var file in observedInput.OrderBy(item => item.Path.ToString(PathTable)))
                            {
                                writer.WritePropertyName(file.Path.ToString(CachedGraph.Context.PathTable).ToUpperInvariant());

                                writer.WriteStartObject();
                                WriteJsonProperty(writer, "ContentHash", file.Hash.ToString());
                                WriteJsonProperty(writer, "Type", file.Type.ToString());
                                WriteJsonProperty(writer, "DirectoryEnumeration", file.DirectoryEnumeration);
                                WriteJsonProperty(writer, "IsDirectoryPath", file.IsDirectoryPath);
                                WriteJsonProperty(writer, "IsSearchPath", file.IsSearchPath);
                                writer.WriteEndObject();
                            }
                        }
                        writer.WriteEndObject();

                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
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
