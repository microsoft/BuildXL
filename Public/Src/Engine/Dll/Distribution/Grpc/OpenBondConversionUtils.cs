// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Google.Protobuf;
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
                ConfigState = cachedGraphDescriptor.ConfigState?.Data.ToByteString() ?? ByteString.Empty,
                DirectedGraph = cachedGraphDescriptor.DirectedGraph?.Data.ToByteString() ?? ByteString.Empty,
                EngineState = cachedGraphDescriptor.EngineState?.Data.ToByteString() ?? ByteString.Empty,
                HistoricTableSizes = cachedGraphDescriptor.HistoricTableSizes?.Data.ToByteString() ?? ByteString.Empty,
                MountPathExpander = cachedGraphDescriptor.MountPathExpander?.Data.ToByteString() ?? ByteString.Empty,
                PathTable = cachedGraphDescriptor.PathTable?.Data.ToByteString() ?? ByteString.Empty,
                PipGraph = cachedGraphDescriptor.PipGraph?.Data.ToByteString() ?? ByteString.Empty,
                PipGraphId = cachedGraphDescriptor.PipGraphId?.Data.ToByteString() ?? ByteString.Empty,
                PipTable = cachedGraphDescriptor.PipTable?.Data.ToByteString() ?? ByteString.Empty,
                PreviousInputs = cachedGraphDescriptor.PreviousInputs?.Data.ToByteString() ?? ByteString.Empty,
                QualifierTable = cachedGraphDescriptor.QualifierTable?.Data.ToByteString() ?? ByteString.Empty,
                StringTable = cachedGraphDescriptor.StringTable?.Data.ToByteString() ?? ByteString.Empty,
                SymbolTable = cachedGraphDescriptor.SymbolTable?.Data.ToByteString() ?? ByteString.Empty,
            };
        }

        public static PipGraphCacheDescriptor ToOpenBond(this BuildXL.Distribution.Grpc.PipGraphCacheDescriptor cachedGraphDescriptor)
        {
            return new Cache.Fingerprints.PipGraphCacheDescriptor()
            {
                ConfigState = cachedGraphDescriptor.ConfigState.ToBondContentHash(),
                DirectedGraph = cachedGraphDescriptor.DirectedGraph.ToBondContentHash(),
                EngineState = cachedGraphDescriptor.EngineState.ToBondContentHash(),
                HistoricTableSizes = cachedGraphDescriptor.HistoricTableSizes.ToBondContentHash(),
                Id = cachedGraphDescriptor.Id,
                MountPathExpander = cachedGraphDescriptor.MountPathExpander.ToBondContentHash(),
                PathTable = cachedGraphDescriptor.PathTable.ToBondContentHash(),
                PipGraph = cachedGraphDescriptor.PipGraph.ToBondContentHash(),
                PipGraphId = cachedGraphDescriptor.PipGraphId.ToBondContentHash(),
                PipTable = cachedGraphDescriptor.PipTable.ToBondContentHash(),
                PreviousInputs = cachedGraphDescriptor.PreviousInputs.ToBondContentHash(),
                QualifierTable = cachedGraphDescriptor.QualifierTable.ToBondContentHash(),
                StringTable = cachedGraphDescriptor.StringTable.ToBondContentHash(),
                SymbolTable = cachedGraphDescriptor.SymbolTable.ToBondContentHash(),
                TraceInfo = cachedGraphDescriptor.TraceInfo
            };
        }
    }
}