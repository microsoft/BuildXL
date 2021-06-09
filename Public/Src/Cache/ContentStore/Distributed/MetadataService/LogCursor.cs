// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using ProtoBuf;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    [ProtoContract]
    public struct LogCursor
    {
        [ProtoMember(1)]
        public CheckpointLogId LogId { get; init; }

        [ProtoMember(2)]
        public int LogBlockId { get; init; }

        [ProtoMember(3)]
        public int SequenceNumber { get; init; }
    }
}
