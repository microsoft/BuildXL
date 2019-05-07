// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Info related to storing a two-phase cache entry for this process execution.
    /// </summary>
    public sealed class TwoPhaseCachingInfo
    {
        /// <nodoc />
        public readonly WeakContentFingerprint WeakFingerprint;

        /// <nodoc />
        public readonly ContentHash PathSetHash;

        /// <nodoc />
        public readonly StrongContentFingerprint StrongFingerprint;

        /// <nodoc />
        public readonly CacheEntry CacheEntry;

        private static readonly int s_maxContentHashFingerprintLength = Math.Max(ContentHash.SerializedLength, Fingerprint.MaxLength);

        /// <nodoc />
        public TwoPhaseCachingInfo(
            WeakContentFingerprint weakFingerprint, 
            ContentHash pathSetHash, 
            StrongContentFingerprint strongFingerprint, 
            CacheEntry cacheEntry)
        {
            WeakFingerprint = weakFingerprint;
            PathSetHash = pathSetHash;
            StrongFingerprint = strongFingerprint;
            CacheEntry = cacheEntry;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return FormattableStringEx.I($"WF:{WeakFingerprint}, PS#:{PathSetHash}, SF:{StrongFingerprint}, MD#:{CacheEntry.MetadataHash}");
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            var buffer = new byte[s_maxContentHashFingerprintLength];
            SerializeFingerprint(writer, WeakFingerprint.Hash, buffer);
            SerializeHash(writer, PathSetHash, buffer);
            SerializeFingerprint(writer, StrongFingerprint.Hash, buffer);
            SerializeCacheEntry(writer, CacheEntry, buffer);
        }

        /// <nodoc />
        public static TwoPhaseCachingInfo Deserialize(BuildXLReader reader)
        {
            var buffer = new byte[s_maxContentHashFingerprintLength];

            WeakContentFingerprint weakFingerprint = new WeakContentFingerprint(DeserializeFingerprint(reader, buffer));
            ContentHash pathSetHash = DeserializeHash(reader, buffer);
            StrongContentFingerprint strongFingerprint = new StrongContentFingerprint(DeserializeFingerprint(reader, buffer));
            CacheEntry cacheEntry = DeserializeCacheEntry(reader, buffer);

            return new TwoPhaseCachingInfo(weakFingerprint, pathSetHash, strongFingerprint, cacheEntry);
        }

        private static void SerializeCacheEntry(BuildXLWriter writer, in CacheEntry entry, byte[] buffer)
        {
            if (!string.IsNullOrEmpty(entry.OriginatingCache))
            {
                writer.Write(true);
                writer.Write(entry.OriginatingCache);
            }
            else
            {
                writer.Write(false);
            }

            SerializeHash(writer, entry.MetadataHash, buffer);
            writer.WriteCompact(entry.ReferencedContent.Length);
            for (int i = 0; i < entry.ReferencedContent.Length; i++)
            {
                SerializeHash(writer, entry.ReferencedContent[i], buffer);
            }
        }

        private static CacheEntry DeserializeCacheEntry(BuildXLReader reader, byte[] buffer)
        {
            string originatingCache = null;
            if (reader.ReadBoolean())
            {
                originatingCache = reader.ReadString();
            }

            var metadataHash = DeserializeHash(reader, buffer);
            var referencedContent = CollectionUtilities.NewOrEmptyArray<ContentHash>(reader.ReadInt32Compact());
            for (int i = 0; i < referencedContent.Length; i++)
            {
                referencedContent[i] = DeserializeHash(reader, buffer);
            }

            return new CacheEntry(metadataHash, originatingCache, referencedContent);
        }

        private static void SerializeHash(BuildXLWriter writer, ContentHash hash, byte[] buffer)
        {
            hash.Serialize(buffer, 0, ContentHash.SerializeHashBytesMethod.Full);
            writer.Write(buffer, 0, ContentHash.SerializedLength);
        }

        private static ContentHash DeserializeHash(BuildXLReader reader, byte[] buffer)
        {
            reader.Read(buffer, 0, ContentHash.SerializedLength);
            return new ContentHash(buffer, serializeMethod: ContentHash.SerializeHashBytesMethod.Full);
        }

        private static void SerializeFingerprint(BuildXLWriter writer, Fingerprint fingerprint, byte[] buffer)
        {
            writer.WriteCompact(fingerprint.Length);
            fingerprint.Serialize(buffer, 0);
            writer.Write(buffer, 0, fingerprint.Length);
        }

        private static Fingerprint DeserializeFingerprint(BuildXLReader reader, byte[] buffer)
        {
            var length = reader.ReadInt32Compact();
            reader.Read(buffer, 0, length);
            return new Fingerprint(buffer, length);
        }
    }
}