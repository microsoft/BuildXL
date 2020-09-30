// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.PackedTable;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.PackedExecution
{
    /// <summary>
    /// Boilerplate ID type to avoid ID confusion in code.
    /// </summary>
    public struct DirectoryId : Id<DirectoryId>, IEqualityComparer<DirectoryId>
    {
        /// <summary>Value as int.</summary>
        public readonly int Value;
        /// <summary>Constructor.</summary>
        public DirectoryId(int value) { Id<StringId>.CheckNotZero(value); Value = value; }
        /// <summary>Eliminator.</summary>
        public int FromId() => Value;
        /// <summary>Introducer.</summary>
        public DirectoryId ToId(int value) => new DirectoryId(value);
        /// <summary>Debugging.</summary>
        public override string ToString() => $"DirectoryId[{Value}]";
        /// <summary>Comparison.</summary>
        public bool Equals([AllowNull] DirectoryId x, [AllowNull] DirectoryId y) => x.Value == y.Value;
        /// <summary>Hashing.</summary>
        public int GetHashCode([DisallowNull] DirectoryId obj) => obj.Value;
    }

    /// <summary>
    /// Information about a single file.
    /// </summary>
    public struct DirectoryEntry 
    {
        /// <summary>
        /// The directory path.
        /// </summary>
        public readonly NameId Path;
        /// <summary>
        /// The producing pip.
        /// </summary>
        public readonly PipId ProducerPip;
        /// <summary>
        /// The content flags for this directory.
        /// </summary>
        public readonly ContentFlags ContentFlags;

        /// <summary>
        /// Construct a DirectoryEntry.
        /// </summary>
        public DirectoryEntry(NameId path, PipId producerPip, ContentFlags contentFlags)
        { 
            Path = path;
            ProducerPip = producerPip;
            ContentFlags = contentFlags;
        }

        /// <summary>
        /// Construct a DirectoryEntry with replaced content flags.
        /// </summary>
        public DirectoryEntry WithContentFlags(ContentFlags contentFlags) { return new DirectoryEntry(Path, ProducerPip, contentFlags); }

        /// <summary>
        /// Equality comparison.
        /// </summary>
        public struct EqualityComparer : IEqualityComparer<DirectoryEntry>
        {
            /// <summary>
            /// Equality.
            /// </summary>
            public bool Equals(DirectoryEntry x, DirectoryEntry y) => x.Path.Equals(y.Path);
            /// <summary>
            /// Hashing.
            /// </summary>
            /// <param name="obj"></param>
            /// <returns></returns>
            public int GetHashCode([DisallowNull] DirectoryEntry obj) => obj.Path.GetHashCode();
        }
    }

    /// <summary>
    /// Table of all files.
    /// </summary>
    /// <remarks>
    /// Every single file in an XLG trace goes into this one table.
    /// </remarks>
    public class DirectoryTable : SingleValueTable<DirectoryId, DirectoryEntry>
    {
        /// <summary>
        /// The names of files in this DirectoryTable.
        /// </summary>
        /// <remarks>
        /// This sub-table is owned by this DirectoryTable; the DirectoryTable constructs it, and saves and loads it.
        /// </remarks>
        public readonly NameTable PathTable;

        /// <summary>
        /// Construct a DirectoryTable given an underlying path table.
        /// </summary>
        public DirectoryTable(NameTable pathTable, int capacity = DefaultCapacity) : base(capacity)
        {
            PathTable = pathTable;
        }

        /// <summary>
        /// Build Directories, caching the paths.
        /// </summary>
        public class CachingBuilder : CachingBuilder<DirectoryEntry.EqualityComparer>
        {
            /// <summary>
            /// The builder for the underlying PathTable.
            /// </summary>
            public readonly NameTable.Builder PathTableBuilder;

            /// <summary>
            /// Construct a CachingBuilder.
            /// </summary>
            public CachingBuilder(DirectoryTable table, NameTable.Builder pathTableBuilder) : base(table)
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
            public DirectoryId GetOrAdd(string directoryPath, PipId producerPip, ContentFlags contentFlags)
            {
                DirectoryEntry entry = new DirectoryEntry(PathTableBuilder.GetOrAdd(directoryPath), producerPip, contentFlags);
                return GetOrAdd(entry);
            }
        }
    }
}
