using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{

    /// <summary>
    /// Metadata that is stored inside the <see cref="Columns.Metadata"/> column family.
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

        public MetadataEntry(ContentHashListWithDeterminism contentHashListWithDeterminism, long lastAccessTimeUtc)
        {
            ContentHashListWithDeterminism = contentHashListWithDeterminism;
            LastAccessTimeUtc = lastAccessTimeUtc;
        }

        public static MetadataEntry Deserialize(BuildXLReader reader)
        {
            var lastUpdateTimeUtc = reader.ReadInt64Compact();
            var contentHashListWithDeterminism = ContentHashListWithDeterminism.Deserialize(reader);
            return new MetadataEntry(contentHashListWithDeterminism, lastUpdateTimeUtc);
        }

        public static long DeserializeLastAccessTimeUtc(BuildXLReader reader)
        {
            return reader.ReadInt64Compact();
        }

        public void Serialize(BuildXLWriter writer)
        {
            writer.WriteCompact(LastAccessTimeUtc);
            ContentHashListWithDeterminism.Serialize(writer);
        }
    }
}
