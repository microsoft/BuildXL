// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities.PackedTable;

namespace BuildXL.Utilities.PackedExecution
{
    /// <summary>
    /// IDs of files; corresponds to BuildXL FileArtifact.
    /// </summary>
    public readonly struct FileId : Id<FileId>, IEquatable<FileId>
    {
        /// <nodoc />
        public struct EqualityComparer : IEqualityComparer<FileId>
        {
            /// <nodoc />
            public bool Equals(FileId x, FileId y) => x.Value == y.Value;
            /// <nodoc />
            public int GetHashCode(FileId obj) => obj.Value;
        }

        /// <summary>A global comparer to avoid boxing allocation on each usage</summary>
        public static IEqualityComparer<FileId> EqualityComparerInstance { get; } = new EqualityComparer();

        /// <summary>Value as int.</summary>
        public int Value { get; }

        /// <nodoc />
        public FileId(int value)
        { 
            Id<FileId>.CheckValidId(value);
            Value = value;
        }

        /// <nodoc />
        public FileId CreateFrom(int value) => new(value);

        /// <nodoc />
        public override string ToString() => $"FileId[{Value}]";

        /// <nodoc />
        public static bool operator ==(FileId x, FileId y) => x.Value == y.Value;

        /// <nodoc />
        public static bool operator !=(FileId x, FileId y) => !(x == y);

        /// <nodoc />
        public IEqualityComparer<FileId> Comparer => EqualityComparerInstance;

        /// <nodoc />
        public int CompareTo([AllowNull] FileId other) => Value.CompareTo(other.Value);

        /// <inheritdoc />
        public override bool Equals(object obj) => StructUtilities.Equals(this, obj);

        /// <inheritdoc />
        public bool Equals(FileId other) => Value == other.Value;

        /// <inheritdoc />
        public override int GetHashCode() => Value;

    }

    /// <summary>
    /// 256-bit (max) file hash, encoded as four ulongs.
    /// </summary>
    /// <remarks>
    /// It appears that the VSO0 33-byte hash actually has zero as the last byte almost all the time, so 32 bytes
    /// seems adequate in practice.
    /// </remarks>
    public readonly struct FileHash
    {
        /// <summary>First 64 bits of hash.</summary>
        public readonly ulong Data0;
        /// <summary>Second 64 bits of hash.</summary>
        public readonly ulong Data1;
        /// <summary>Third 64 bits of hash.</summary>
        public readonly ulong Data2;
        /// <summary>Fourth 64 bits of hash.</summary>
        public readonly ulong Data3;

        /// <summary>Construct a FileHash from a ulong[4] array.</summary>
        public FileHash(ulong[] hashBuffer)
        {
            Data0 = hashBuffer[0];
            Data1 = hashBuffer[1];
            Data2 = hashBuffer[2];
            Data3 = hashBuffer[3];
        }
    }

    /// <summary>
    /// Information about a single file.
    /// </summary>
    public readonly struct FileEntry 
    {
        /// <summary>The file's path.</summary>
        /// <remarks>
        /// Since paths are long hierarchical names with lots of sharing with other paths, we use a
        /// NameTable to store them, and hence the path is identified by NameId.
        /// </remarks>
        public readonly NameId Path;
        /// <summary>File size in bytes.</summary>
        public readonly long SizeInBytes;
        /// <summary>The file's content flags.</summary>
        public readonly ContentFlags ContentFlags;
        /// <summary>The file's content hash.</summary>
        public readonly FileHash Hash;
        /// <summary>The file's rewrite count (see </summary>
        public readonly int RewriteCount;

        /// <summary>
        /// Construct a FileEntry.
        /// </summary>
        public FileEntry(NameId name, long sizeInBytes, ContentFlags contentFlags, FileHash hash, int rewriteCount)
        { 
            Path = name;
            SizeInBytes = sizeInBytes;
            ContentFlags = contentFlags;
            Hash = hash;
            RewriteCount = rewriteCount;
        }

        /// <summary>
        /// Create a clone of this FileEntry with updated content flags.
        /// </summary>
        public FileEntry WithContentFlags(ContentFlags contentFlags)
            => new FileEntry(Path, SizeInBytes, contentFlags, Hash, RewriteCount);

        /// <summary>
        /// Equality comparison (based on path, not hash).
        /// </summary>
        public struct EqualityComparer : IEqualityComparer<FileEntry>
        {
            /// <summary>
            /// Equality.
            /// </summary>
            public bool Equals(FileEntry x, FileEntry y) => x.Path == y.Path && x.RewriteCount == y.RewriteCount;
            /// <summary>
            /// Hashing.
            /// </summary>
            public int GetHashCode([DisallowNull] FileEntry obj) => obj.Path.GetHashCode() ^ obj.RewriteCount.GetHashCode();
        }
    }

    /// <summary>
    /// Table of all files.
    /// </summary>
    /// <remarks>
    /// Every single file in an XLG trace goes into this one table.
    /// </remarks>
    public class FileTable : SingleValueTable<FileId, FileEntry>
    {
        /// <summary>
        /// The pathnames of files in this FileTable.
        /// </summary>
        /// <remarks>
        /// This table is shared between this table and the DirectoryTable.
        /// </remarks>
        public readonly NameTable PathTable;

        /// <summary>
        /// Construct a FileTable.
        /// </summary>
        public FileTable(NameTable pathTable, int capacity = DefaultCapacity) : base(capacity)
        {
            PathTable = pathTable;
        }

        /// <summary>
        /// Caching builder for creating path-unique Files.
        /// </summary>
        public class CachingBuilder : CachingBuilder<FileEntry.EqualityComparer>
        {
            /// <summary>
            /// The builder for the underlying PathTable.
            /// </summary>
            public readonly NameTable.Builder PathTableBuilder;

            /// <summary>
            /// Function for merging two entries together to unify their content flags.
            /// </summary>
            private readonly Func<FileEntry, FileEntry, FileEntry> m_mergeFunc = (oldEntry, newEntry) =>
            {
                // Produced > MaterializedFromCache > Materialized.
                ContentFlags oldFlags = oldEntry.ContentFlags;
                ContentFlags newFlags = newEntry.ContentFlags;
                bool eitherProduced = ((oldFlags & ContentFlags.Produced) != 0 || (newFlags & ContentFlags.Produced) != 0);
                bool eitherMaterializedFromCache = ((oldFlags & ContentFlags.MaterializedFromCache) != 0 || (newFlags & ContentFlags.MaterializedFromCache) != 0);
                bool eitherMaterialized = ((oldFlags & ContentFlags.Materialized) != 0 || (newFlags & ContentFlags.Materialized) != 0);

                return newEntry.WithContentFlags(
                    eitherProduced
                        ? ContentFlags.Produced
                        : eitherMaterializedFromCache
                            ? ContentFlags.MaterializedFromCache
                            : eitherMaterialized
                                ? ContentFlags.Materialized
                                : default);
            };

            /// <summary>
            /// Construct a CachingBuilder.
            /// </summary>
            public CachingBuilder(FileTable table, NameTable.Builder pathTableBuilder) : base(table)
            {
                PathTableBuilder = pathTableBuilder;
            }

            /// <summary>
            /// Get or add an entry for the given file path.
            /// </summary>
            /// <remarks>
            /// If the entry already exists, its existing data will be retained, and the arguments passed here 
            /// will be ignored.
            /// </remarks>
            public FileId GetOrAdd(string filePath, long sizeInBytes, ContentFlags contentFlags, FileHash hash, int rewriteCount)
            {
                FileEntry entry = new FileEntry(
                    PathTableBuilder.GetOrAdd(filePath),
                    sizeInBytes,
                    contentFlags,
                    hash,
                    rewriteCount);
                return GetOrAdd(entry);
            }

            /// <summary>
            /// Update or add an entry for the given file path.
            /// </summary>
            /// <remarks>
            /// If the entry already exists, its content flags will be merged with these, and the other file attributes
            /// will be updated to the values passed here.
            /// </remarks>
            public FileId UpdateOrAdd(string filePath, long sizeInBytes, ContentFlags contentFlags, FileHash hash, int rewriteCount)
            {
                FileEntry entry = new FileEntry(
                    PathTableBuilder.GetOrAdd(filePath),
                    sizeInBytes,
                    contentFlags,
                    hash,
                    rewriteCount);
                return UpdateOrAdd(entry, m_mergeFunc);
            }
        }
    }
}
