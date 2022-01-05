// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using ProtoBuf;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    [ProtoContract]
    public struct BlockReference : IComparable<BlockReference>
    {
        [ProtoMember(1)]
        public CheckpointLogId LogId { get; init; }

        [ProtoMember(2)]
        public int LogBlockId { get; init; }

        public static BlockReference MaxValue => new BlockReference(CheckpointLogId.MaxValue, int.MaxValue);

        public BlockReference(CheckpointLogId logId, int logBlockId)
        {
            LogId = logId;
            LogBlockId = logBlockId;
        }

        public static implicit operator BlockReference((CheckpointLogId logId, int blockId) value)
        {
            return new BlockReference()
            {
                LogId = value.logId,
                LogBlockId = value.blockId
            };
        }

        public override string ToString()
        {
            return $"CheckpointLogId=[{LogId}] LogBlockId=[{LogBlockId}]";
        }

        public int CompareTo(BlockReference other)
        {
            return (LogId.IsCompareEquals(other.LogId, out var compareResult) && LogBlockId.IsCompareEquals(other.LogBlockId, out compareResult)) ? 0 : compareResult;
        }
    }
}
