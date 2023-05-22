// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Distributed.NuCache;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService;

public readonly record struct LogCursor
{
    public CheckpointLogId LogId => Block.LogId;
        
    public int LogBlockId => Block.LogBlockId;

    public BlockReference Block { get; init; }
        
    public int SequenceNumber { get; init; }

    public override string ToString()
    {
        return $"{Block} SequenceNumber=[{SequenceNumber}]";
    }
}
