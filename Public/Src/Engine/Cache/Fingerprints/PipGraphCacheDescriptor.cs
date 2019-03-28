// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <summary>
    /// Descriptor for a cached pip graph.
    /// </summary>
    public partial class PipGraphCacheDescriptor : IPipFingerprintEntryData
    {
        /// <nodoc />
        public PipFingerprintEntryKind Kind => PipFingerprintEntryKind.GraphDescriptor;

        /// <inheritdoc />
        public IEnumerable<BondContentHash> ListRelatedContent()
        {
            return EnumerateGraphFiles().Select(kvp => kvp.Value);
        }

        /// <nodoc />
        public PipFingerprintEntry ToEntry()
        {
            return PipFingerprintEntry.CreateFromData(this);
        }

        /// <nodoc />
        public static PipGraphCacheDescriptor CreateFromFiles(
            IDictionary<GraphCacheFile, BondContentHash> files,
            string traceInfo)
        {
            var descriptor = new PipGraphCacheDescriptor
            {
                Id = PipFingerprintEntry.CreateUniqueId(),
                TraceInfo = traceInfo,
            };

            foreach (var kvp in files)
            {
                switch (kvp.Key)
                {
                    case GraphCacheFile.PreviousInputs:
                        descriptor.PreviousInputs = kvp.Value;
                        break;
                    case GraphCacheFile.PipTable:
                        descriptor.PipTable = kvp.Value;
                        break;
                    case GraphCacheFile.PathTable:
                        descriptor.PathTable = kvp.Value;
                        break;
                    case GraphCacheFile.StringTable:
                        descriptor.StringTable = kvp.Value;
                        break;
                    case GraphCacheFile.SymbolTable:
                        descriptor.SymbolTable = kvp.Value;
                        break;
                    case GraphCacheFile.QualifierTable:
                        descriptor.QualifierTable = kvp.Value;
                        break;
                    case GraphCacheFile.MountPathExpander:
                        descriptor.MountPathExpander = kvp.Value;
                        break;
                    case GraphCacheFile.ConfigState:
                        descriptor.ConfigState = kvp.Value;
                        break;
                    case GraphCacheFile.DirectedGraph:
                        descriptor.DirectedGraph = kvp.Value;
                        break;
                    case GraphCacheFile.PipGraph:
                        descriptor.PipGraph = kvp.Value;
                        break;
                    case GraphCacheFile.PipGraphId:
                        descriptor.PipGraphId = kvp.Value;
                        break;
                    case GraphCacheFile.HistoricTableSizes:
                        descriptor.HistoricTableSizes = kvp.Value;
                        break;
                    default:
                        throw Contract.AssertFailure("Unhandled GraphCacheFile");
                }
            }

            return descriptor;
        }

        /// <nodoc />
        public IEnumerable<KeyValuePair<GraphCacheFile, BondContentHash>> EnumerateGraphFiles()
        {
            yield return new KeyValuePair<GraphCacheFile, BondContentHash>(GraphCacheFile.PathTable, PathTable);
            yield return new KeyValuePair<GraphCacheFile, BondContentHash>(GraphCacheFile.StringTable, StringTable);
            yield return new KeyValuePair<GraphCacheFile, BondContentHash>(GraphCacheFile.SymbolTable, SymbolTable);
            yield return new KeyValuePair<GraphCacheFile, BondContentHash>(GraphCacheFile.QualifierTable, QualifierTable);
            yield return new KeyValuePair<GraphCacheFile, BondContentHash>(GraphCacheFile.PipTable, PipTable);
            yield return new KeyValuePair<GraphCacheFile, BondContentHash>(GraphCacheFile.PreviousInputs, PreviousInputs);
            yield return new KeyValuePair<GraphCacheFile, BondContentHash>(GraphCacheFile.MountPathExpander, MountPathExpander);
            yield return new KeyValuePair<GraphCacheFile, BondContentHash>(GraphCacheFile.ConfigState, ConfigState);
            yield return new KeyValuePair<GraphCacheFile, BondContentHash>(GraphCacheFile.DirectedGraph, DirectedGraph);
            yield return new KeyValuePair<GraphCacheFile, BondContentHash>(GraphCacheFile.PipGraph, PipGraph);
            yield return new KeyValuePair<GraphCacheFile, BondContentHash>(GraphCacheFile.PipGraphId, PipGraphId);
            yield return new KeyValuePair<GraphCacheFile, BondContentHash>(GraphCacheFile.HistoricTableSizes, HistoricTableSizes);
        }
    }
}
