// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{

    /// <summary>
    /// Metadata entry for memoization stores.
    /// </summary>
    public readonly struct MetadataEntry
    {
        /// <summary>
        /// Effective <see cref="ContentHashList"/> that we want to store, along with information about its cache
        /// determinism.
        /// </summary>
        public ContentHashListWithDeterminism ContentHashListWithDeterminism { get; }

        /// <summary>
        /// Last update time, stored as output by <see cref="DateTime.ToFileTimeUtc"/>.
        /// </summary>
        public long LastAccessTimeUtc { get; }

        /// <nodoc />
        public MetadataEntry(ContentHashListWithDeterminism contentHashListWithDeterminism, long lastAccessTimeUtc)
        {
            ContentHashListWithDeterminism = contentHashListWithDeterminism;
            LastAccessTimeUtc = lastAccessTimeUtc;
        }

        /// <nodoc />
        public static MetadataEntry Deserialize(BuildXLReader reader)
        {
            var lastUpdateTimeUtc = reader.ReadInt64Compact();
            var contentHashListWithDeterminism = ContentHashListWithDeterminism.Deserialize(reader);
            return new MetadataEntry(contentHashListWithDeterminism, lastUpdateTimeUtc);
        }

        /// <nodoc />
        public static long DeserializeLastAccessTimeUtc(BuildXLReader reader)
        {
            return reader.ReadInt64Compact();
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            writer.WriteCompact(LastAccessTimeUtc);
            ContentHashListWithDeterminism.Serialize(writer);
        }
    }
}
