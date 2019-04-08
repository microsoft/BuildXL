// Copyright (c) Microsoft Corporation. All rights reserved.

using System.CodeDom.Compiler;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable SA1600
#pragma warning disable SA1602

namespace BuildXL.Cache.ContentStore.Hashing.Chunking
{
    //[GeneratedCode("Copied from Windows Sources", "1.0")]
    public enum DedupChunkCutType
    {
        DDP_CCT_Unknown = 0,
        DDP_CCT_EndReached_And_Partial = 1, // Partial chunk smaller than MIN_CHUNK_SIZE, only emit at the end of chunking.
                                            // Should not receive any further call to the chunking module.
        DDP_CCT_MinReached = 2,             // Small chunk, chunk is of size MIN_CHUNK_SIZE
        DDP_CCT_Normal = 3,                 // Normal chunk (chunk end established by the normal chunking algorithm)
        DDP_CCT_MaxReached = 4,             // Max-sized chunk (chunk end established by max size, as the normal chunking algorithm failed to find a cut-off point before the max)
        DDP_CCT_EndReached = 5,             // Reach end of file
        DDP_CCT_Transition = 6,             // Chunk for detecting transition between a non-repeated region to a repeated region.
        DDP_CCT_Regress_1_bit = 7,          // "Regress" chunk - i.e. chunking was done using a partial mask, 7: using mask of -1 bit.
        DDP_CCT_Regress_2_bit = 8,          // 8: using mask of -2 bit.
        DDP_CCT_Regress_3_bit = 9,          // 9: using mask of -3 bit
        DDP_CCT_Regress_4_bit = 10,         // 10: using mask of -4 bit.
        DDP_CCT_Chunk_by_16Zeros = 11,      // We encountered a block of zeros (more than 16), the chunking point will be the first non-zero point.
        DDP_CCT_All_Zero = 12,              // 11: all zero chunks.
        DDP_CCT_End = 12,                   // Shouldn't be used.
    }
}
