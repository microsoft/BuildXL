// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using PipGraphCacheDescriptor = BuildXL.Engine.Cache.Fingerprints.PipGraphCacheDescriptor;

namespace BuildXL.Engine.Distribution.Grpc
{
    internal static class OpenBondConversionUtils
    {
        public static BuildXL.Distribution.Grpc.PipGraphCacheDescriptor ToGrpc(this PipGraphCacheDescriptor cachedGraphDescriptor)
        {
            return new BuildXL.Distribution.Grpc.PipGraphCacheDescriptor()
            {
                Id = cachedGraphDescriptor.Id,
                TraceInfo = cachedGraphDescriptor.TraceInfo,
                ConfigState = cachedGraphDescriptor.ConfigState,
                DirectedGraph = cachedGraphDescriptor.DirectedGraph,
                EngineState = cachedGraphDescriptor.EngineState,
                HistoricTableSizes = cachedGraphDescriptor.HistoricTableSizes,
                MountPathExpander = cachedGraphDescriptor.MountPathExpander,
                PathTable = cachedGraphDescriptor.PathTable,
                PipGraph = cachedGraphDescriptor.PipGraph,
                PipGraphId = cachedGraphDescriptor.PipGraphId,
                PipTable = cachedGraphDescriptor.PipTable,
                PreviousInputs = cachedGraphDescriptor.PreviousInputs,
                QualifierTable = cachedGraphDescriptor.QualifierTable,
                StringTable = cachedGraphDescriptor.StringTable,
                SymbolTable = cachedGraphDescriptor.SymbolTable,
            };
        }

        public static PipGraphCacheDescriptor ToCacheGrpc(this BuildXL.Distribution.Grpc.PipGraphCacheDescriptor cachedGraphDescriptor)
        {
            return new Cache.Fingerprints.PipGraphCacheDescriptor()
            {
                ConfigState = cachedGraphDescriptor.ConfigState,
                DirectedGraph = cachedGraphDescriptor.DirectedGraph,
                EngineState = cachedGraphDescriptor.EngineState,
                HistoricTableSizes = cachedGraphDescriptor.HistoricTableSizes,
                Id = cachedGraphDescriptor.Id,
                MountPathExpander = cachedGraphDescriptor.MountPathExpander,
                PathTable = cachedGraphDescriptor.PathTable,
                PipGraph = cachedGraphDescriptor.PipGraph   ,
                PipGraphId = cachedGraphDescriptor.PipGraphId,
                PipTable = cachedGraphDescriptor.PipTable,
                PreviousInputs = cachedGraphDescriptor.PreviousInputs,
                QualifierTable = cachedGraphDescriptor.QualifierTable,
                StringTable = cachedGraphDescriptor.StringTable,
                SymbolTable = cachedGraphDescriptor.SymbolTable,
                TraceInfo = cachedGraphDescriptor.TraceInfo
            };
        }
    }
}