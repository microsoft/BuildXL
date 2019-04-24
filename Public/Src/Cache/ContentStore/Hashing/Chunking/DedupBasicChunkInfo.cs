// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using size_t = System.UInt64;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable SA1121 // Use built-in type alias
#pragma warning disable SA1210
#pragma warning disable SA1307 // Accessible fields must begin with upper-case letter
#pragma warning disable SA1308 // Variable names must not be prefixed
#pragma warning disable SA1508
#pragma warning disable SA1600

namespace BuildXL.Cache.ContentStore.Hashing.Chunking
{
    //[GeneratedCode("Copied from Windows Sources", "1.0")]
    public struct DedupBasicChunkInfo : IEquatable<DedupBasicChunkInfo>
    {
        // Constructor
        public DedupBasicChunkInfo(size_t startChunk, size_t chunkLength, DedupChunkCutType type)
        {
            m_nStartChunk = startChunk;
            m_nChunkLength = chunkLength;
            m_eCutType = type;
        }

        public readonly size_t m_nStartChunk;               // Offset of the chunk in the buffer
        public readonly size_t m_nChunkLength;              // Chunk length
        public readonly DedupChunkCutType m_eCutType;       // Cut type for the end of the chunk

        /// <inheritdoc />
        public override string ToString()
        {
            return $"Offset:{m_nStartChunk} Length:{m_nChunkLength} CutType:{m_eCutType}";
        }

        public override int GetHashCode()
        {
            return (int)m_nStartChunk ^ (int)m_nChunkLength ^ (int)m_eCutType;
        }

        public override bool Equals(object obj)
        {
            if (obj is DedupBasicChunkInfo)
            {
                return Equals((DedupBasicChunkInfo)obj);
            }
            else
            {
                return false;
            }
        }

        public bool Equals(DedupBasicChunkInfo other)
        {
            return m_nStartChunk == other.m_nStartChunk
                && m_nChunkLength == other.m_nChunkLength
                && m_eCutType == other.m_eCutType;
        }

        public static bool operator ==(DedupBasicChunkInfo chunk1, DedupBasicChunkInfo chunk2)
        {
            return chunk1.Equals(chunk2);
        }

        public static bool operator !=(DedupBasicChunkInfo chunk1, DedupBasicChunkInfo chunk2)
        {
            return !chunk1.Equals(chunk2);
        }
    }
}
