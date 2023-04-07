// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using Google.Protobuf;

namespace BuildXL.Engine.Cache.Fingerprints
{   /// <summary>
    /// Descriptor for a cached pip graph.
    /// </summary>
    public partial class PipGraphCacheDescriptor : IPipFingerprintEntryData
    {    /// <summary>
    /// Descriptor for a cached pip graph.
    /// </summary>
        /// <inheritdoc />
        public IEnumerable<ByteString> ListRelatedContent()
        {
            return EnumerateGraphFiles().Select(kvp => kvp.Value);
        }

        /// <inheritdoc />
        public PipFingerprintEntry ToEntry()
        {
            return PipFingerprintEntry.CreateFromData(PipFingerprintEntryKind.GraphDescriptor, this.ToByteString());
        }

        /// <nodoc />
        public static PipGraphCacheDescriptor CreateFromFiles(
            IDictionary<GraphCacheFile, ByteString> files,
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
        public IEnumerable<KeyValuePair<GraphCacheFile, ByteString>> EnumerateGraphFiles()
        {
            yield return new KeyValuePair<GraphCacheFile, ByteString>(GraphCacheFile.PathTable, PathTable);
            yield return new KeyValuePair<GraphCacheFile, ByteString>(GraphCacheFile.StringTable, StringTable);
            yield return new KeyValuePair<GraphCacheFile, ByteString>(GraphCacheFile.SymbolTable, SymbolTable);
            yield return new KeyValuePair<GraphCacheFile, ByteString>(GraphCacheFile.QualifierTable, QualifierTable);
            yield return new KeyValuePair<GraphCacheFile, ByteString>(GraphCacheFile.PipTable, PipTable);
            yield return new KeyValuePair<GraphCacheFile, ByteString>(GraphCacheFile.PreviousInputs, PreviousInputs);
            yield return new KeyValuePair<GraphCacheFile, ByteString>(GraphCacheFile.MountPathExpander, MountPathExpander);
            yield return new KeyValuePair<GraphCacheFile, ByteString>(GraphCacheFile.ConfigState, ConfigState);
            yield return new KeyValuePair<GraphCacheFile, ByteString>(GraphCacheFile.DirectedGraph, DirectedGraph);
            yield return new KeyValuePair<GraphCacheFile, ByteString>(GraphCacheFile.PipGraph, PipGraph);
            yield return new KeyValuePair<GraphCacheFile, ByteString>(GraphCacheFile.PipGraphId, PipGraphId);
            yield return new KeyValuePair<GraphCacheFile, ByteString>(GraphCacheFile.HistoricTableSizes, HistoricTableSizes);
        }
    }
}
