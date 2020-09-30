// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.PackedTable;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.PackedExecution
{
    /// <summary>
    /// Boilerplate ID type to avoid ID confusion in code.
    /// </summary>
    public struct FileId : Id<FileId>, IEqualityComparer<FileId>
    {
        /// <summary>Value as int.</summary>
        public readonly int Value;
        /// <summary>Constructor.</summary>
        public FileId(int value) { Id<FileId>.CheckNotZero(value); Value = value; }
        /// <summary>Eliminator.</summary>
        public int FromId() => Value;
        /// <summary>Introducer.</summary>
        public FileId ToId(int value) => new FileId(value);
        /// <summary>Debugging.</summary>
        public override string ToString() => $"FileId[{Value}]";
        /// <summary>Comparison.</summary>
        public bool Equals(FileId x, FileId y) => x.Value == y.Value;
        /// <summary>Hashing.</summary>
        public int GetHashCode(FileId obj) => obj.Value;
    }

    /// <summary>
    /// Information about a single file.
    /// </summary>
    public struct FileEntry 
    {
        /// <summary>
        /// The file's path.
        /// </summary>
        public readonly NameId Path;
        /// <summary>
        /// File size in bytes.
        /// </summary>
        public readonly long SizeInBytes;
        /// <summary>
        /// The pip that produced the file.
        /// </summary>
        public readonly PipId ProducerPip;
        /// <summary>
        /// The file's content flags.
        /// </summary>
        public readonly ContentFlags ContentFlags;

        /// <summary>
        /// Construct a FileEntry.
        /// </summary>
        public FileEntry(NameId name, long sizeInBytes, PipId producerPip, ContentFlags contentFlags)
        { 
            Path = name;
            SizeInBytes = sizeInBytes;
            ProducerPip = producerPip;
            ContentFlags = contentFlags;
        }

        /// <summary>
        /// Construct a new FileEntry with a new producer pip.
        /// </summary>
        public FileEntry WithProducerPip(PipId producerPip) { return new FileEntry(Path, SizeInBytes, producerPip, ContentFlags); }
        /// <summary>
        /// Construct a new FileEntry with new content flags.
        /// </summary>
        public FileEntry WithContentFlags(ContentFlags contentFlags) { return new FileEntry(Path, SizeInBytes, ProducerPip, contentFlags); }

        /// <summary>
        /// Equality comparison.
        /// </summary>
        public struct EqualityComparer : IEqualityComparer<FileEntry>
        {
            /// <summary>
            /// Equality.
            /// </summary>
            public bool Equals(FileEntry x, FileEntry y) => x.Path.Equals(y.Path);
            /// <summary>
            /// Hashing.
            /// </summary>
            public int GetHashCode([DisallowNull] FileEntry obj) => obj.Path.GetHashCode();
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
        /// The names of files in this FileTable.
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

                // System should never tell us the file was both produced and materialized from cache
                //Contract.Assert(!(eitherProduced && eitherMaterializedFromCache));

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
            /// If the entry already exists, the sizeInBytes value passed here will be ignored!
            /// The only time that value can be set is when adding a new file not previously recorded.
            /// TODO: consider failing if this happens?
            /// </remarks>
            public FileId GetOrAdd(string filePath, long sizeInBytes, PipId producerPip, ContentFlags contentFlags)
            {
                FileEntry entry = new FileEntry(
                    PathTableBuilder.GetOrAdd(filePath),
                    sizeInBytes,
                    producerPip,
                    contentFlags);
                return GetOrAdd(entry);
            }

            /// <summary>
            /// Update or add an entry for the given file path.
            /// </summary>
            public FileId UpdateOrAdd(string filePath, long sizeInBytes, PipId producerPip, ContentFlags contentFlags)
            {
                FileEntry entry = new FileEntry(
                    PathTableBuilder.GetOrAdd(filePath),
                    sizeInBytes,
                    producerPip,
                    contentFlags);
                return UpdateOrAdd(entry, m_mergeFunc);
            }
        }
    }
}
