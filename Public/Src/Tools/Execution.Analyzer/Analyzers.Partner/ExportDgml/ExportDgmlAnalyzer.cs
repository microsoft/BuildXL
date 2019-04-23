// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Execution.Analyzer.Analyzers.ExportDgml;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.ToolSupport;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeExportDgmlAnalyzer()
        {
            string dgmlFilePath = null;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    dgmlFilePath = ParseSingletonPathOption(opt, dgmlFilePath);
                }
                else
                {
                    throw Error("Unknown option for fingerprint text analysis: {0}", opt.Name);
                }
            }

            return new ExportDgmlAnalyzer(GetAnalysisInput(), dgmlFilePath);
        }

        private static void WriteExportDgmlAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("ExportDgml Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.ExportDgml), "Generates a dgml file of the pip graph");
            writer.WriteOption("outputFile", "Required. The directory containing the cached pip graph files.", shortName: "o");
        }
    }

    /// <summary>
    /// Exports a JSON structured graph, including per-pip static and execution details.
    /// </summary>
    internal sealed class ExportDgmlAnalyzer : Analyzer
    {
        private readonly string m_dgmlFilePath;

        /// <summary>
        /// Creates an exporter which writes text to <paramref name="output" />.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        internal ExportDgmlAnalyzer(AnalysisInput input, string dgmlFilePath)
            : base(input)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(dgmlFilePath));
            m_dgmlFilePath = dgmlFilePath;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            base.Dispose();
        }

        /// <summary>
        /// Exports the contents of the given pip graph. It is expected to no longer change as of the start of this call.
        /// </summary>
        public override int Analyze()
        {
            DgmlWriter writer = new DgmlWriter();

            Dictionary<PipId, PipReference> concretePipValues = new Dictionary<PipId, PipReference>();

            HashSet<PipReference> allValuePips = new HashSet<PipReference>(PipGraph.RetrievePipReferencesOfType(PipType.Value));

            foreach (var valuePip in allValuePips)
            {
                foreach (var incoming in DataflowGraph.GetIncomingEdges(valuePip.PipId.ToNodeId()))
                {
                    if (!PipTable.GetPipType(incoming.OtherNode.ToPipId()).IsMetaPip())
                    {
                        concretePipValues[valuePip.PipId] = valuePip;
                    }
                }
            }

            foreach (var concretePipValue in concretePipValues.Values)
            {
                var value = ((ValuePip)concretePipValue.HydratePip()).Symbol.ToString(SymbolTable);
                writer.AddNode(new DgmlWriter.Node(concretePipValue.PipId.ToString(), value));

                foreach (var incoming in DataflowGraph.GetIncomingEdges(concretePipValue.PipId.ToNodeId()))
                {
                    var incomingId = incoming.OtherNode.ToPipId();
                    if (concretePipValues.ContainsKey(incomingId))
                    {
                        writer.AddLink(new DgmlWriter.Link(
                            incomingId.ToString(),
                            concretePipValue.PipId.ToString(),
                            label: null));
                    }
                }
            }

            writer.Serialize(m_dgmlFilePath);

            return 0;
        }
    }
}
