// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Handles serialization and deserialization of <see cref="ContentHash"/> values in the XLG,
    /// including content hash interning (deduplication) for size reduction.
    /// </summary>
    /// <remarks>
    /// Encoding format — each content hash reference is written as a single compact integer
    /// whose low bit distinguishes interned from inline hashes:
    ///
    ///   - Interned (low bit 0): value = index left-shifted by 1. The full hash bytes were
    ///     previously written in an AddContentHash side-channel event; the reader looks
    ///     them up by index.
    ///
    ///   - Inline (low bit 1): value = (byteLength left-shifted by 1) | 1, followed by the
    ///     raw hash bytes. Used when the intern table cap has been reached.
    ///
    /// This encoding keeps both cases to a single byte for typical values (indices under 64,
    /// hash lengths up to 33), avoiding the 5-byte cost of a negative sentinel in 7-bit
    /// varint encoding.
    /// </remarks>
    internal static class ContentHashSerializer
    {
        /// <summary>
        /// Maximum number of unique content hashes to intern. Once this limit is reached,
        /// new hashes are written inline instead of being added to the intern table.
        /// At ~41 bytes per entry (including ConcurrentBigSet overhead), this caps the
        /// interning table memory at roughly 100 MB.
        /// </summary>
        internal const int MaxContentHashInternCount = 2_400_000;

        /// <summary>
        /// Per-thread buffer for serializing content hashes to avoid repeated allocations.
        /// Since [ThreadStatic] fields are only initialized by the static constructor on the
        /// first thread, the buffer must be lazily allocated on each thread's first use.
        /// </summary>
        [ThreadStatic]
        private static byte[] s_hashBuffer;

        /// <summary>
        /// Writes a content hash to the event stream. When the writer is a
        /// <see cref="BinaryLogger.EventWriter"/>, the hash is interned (deduplicated)
        /// if the table has capacity; otherwise it is written inline.
        /// For non-EventWriter targets, raw hash bytes are written directly.
        /// </summary>
        public static void WriteContentHash(BuildXLWriter writer, ContentHash hash)
        {
            Contract.Assert(
                hash.HashType == ContentHashingUtilities.HashInfo.HashType,
                $"Content hash interning assumes a single hash algorithm per build, but got {hash.HashType} instead of {ContentHashingUtilities.HashInfo.HashType}");

            if (!(writer is BinaryLogger.EventWriter eventWriter) || eventWriter.SuppressContentHashInterning)
            {
                hash.SerializeHashBytes(writer);
                return;
            }

            int byteLength = hash.ByteLength;
            byte[] buffer = s_hashBuffer ??= new byte[ContentHash.MaxHashByteLength];
            hash.SerializeHashBytes(buffer);

            var key = new BinaryLogger.ContentHashKey(buffer, 0, byteLength);
            bool underCap = eventWriter.ContentHashInternCount < MaxContentHashInternCount;

            if (underCap)
            {
                var result = eventWriter.GetOrAddContentHash(key);

                // Follow the same thread-safety pattern as AddPath/AddStringId:
                // Multiple threads may race to add the same hash. The bool value tracks
                // whether the AddContentHash side-channel event has been queued. When false,
                // this thread writes a (possibly duplicate) AddContentHash event to ensure
                // the reader always sees the definition before any reference.
                if (!result.Item.Value)
                {
                    eventWriter.WriteAddContentHashEvent(key, result.Index, buffer, 0, byteLength);
                }

                eventWriter.WriteCompact(result.Index << 1);
                eventWriter.IncrementContentHashEntries();
            }
            else
            {
                // Over cap — check if the hash was previously interned
                var result = eventWriter.TryGetContentHash(key);
                if (result.IsFound)
                {
                    // Previously interned — write encoded index (low bit 0)
                    eventWriter.WriteCompact(result.Index << 1);
                    eventWriter.IncrementContentHashEntries();
                }
                else
                {
                    // Not interned — write inline (low bit 1) followed by raw bytes
                    eventWriter.WriteCompact((byteLength << 1) | 1);
                    eventWriter.Write(buffer, 0, byteLength);
                    eventWriter.IncrementContentHashOverflow();
                }
            }
        }

        /// <summary>
        /// Reads a content hash from the event stream. When the reader is an
        /// <see cref="BinaryLogReader.EventReader"/> with interning support, decodes
        /// the compact format. Otherwise falls back to the legacy raw-bytes format.
        /// </summary>
        public static ContentHash ReadContentHash(BuildXLReader reader)
        {
            if (!(reader is BinaryLogReader.EventReader eventReader) || !eventReader.HasContentHashInterning)
            {
                return ContentHashingUtilities.CreateFrom(reader);
            }

            int encoded = eventReader.ReadInt32Compact();
            byte[] hashBytes;

            if ((encoded & 1) == 0)
            {
                // Interned: index is in the upper bits
                hashBytes = eventReader.GetContentHashBytes(encoded >> 1);
            }
            else
            {
                // Inline: byte length is in the upper bits
                int length = encoded >> 1;
                hashBytes = eventReader.ReadBytes(length);
            }

            return new ContentHash(ContentHashingUtilities.HashInfo.HashType, hashBytes);
        }

        /// <summary>
        /// Reads a content hash for the <see cref="DirectoryMembershipHashedEventData"/> event,
        /// which used a different legacy format (type byte + hash bytes) than other events.
        /// </summary>
        public static ContentHash ReadDirectoryMembershipHash(BuildXLReader reader)
        {
            if (reader is BinaryLogReader.EventReader eventReader && eventReader.HasContentHashInterning)
            {
                return ReadContentHash(reader);
            }

            // Legacy format: ContentHash.Serialize wrote 1 type byte + 33 hash bytes (34 total),
            // unlike other events that used SerializeHashBytes (hash bytes only).
            return new ContentHash(reader);
        }
    }
}
