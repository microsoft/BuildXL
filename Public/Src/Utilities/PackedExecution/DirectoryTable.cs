// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.PackedTable;

namespace BuildXL.Utilities.PackedExecution
{
    /// <summary>
    /// IDs of directories; corresponds to BuildXL DirectoryArtifact.
    /// </summary>
#pragma warning disable CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
    public struct DirectoryId : Id<DirectoryId>
#pragma warning restore CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
#pragma warning restore CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
    {
        /// <nodoc />
        public readonly struct EqualityComparer : IEqualityComparer<DirectoryId>
        {
            /// <nodoc />
            public bool Equals(DirectoryId x, DirectoryId y) => x.Value == y.Value;
            /// <nodoc />
            public int GetHashCode(DirectoryId obj) => obj.Value;
        }

        private readonly int m_value;

        /// <nodoc />
        public int Value => m_value;

        /// <nodoc />
        public DirectoryId(int value)
        {
            Id<DirectoryId>.CheckValidId(value);
            m_value = value;
        }

        /// <nodoc />
        public DirectoryId CreateFrom(int value) => new(value);

        /// <nodoc />
        public override string ToString() => $"DirectoryId[{Value}]";

        /// <nodoc />
        public static bool operator ==(DirectoryId x, DirectoryId y) => x.Value == y.Value;

        /// <nodoc />
        public static bool operator !=(DirectoryId x, DirectoryId y) => !(x == y);

        /// <nodoc />
        public IEqualityComparer<DirectoryId> Comparer => default(EqualityComparer);

        /// <nodoc />
        public int CompareTo([AllowNull] DirectoryId other) => Value.CompareTo(other.Value);
    }

    /// <summary>
    /// Information about a single directory.
    /// </summary>
    public readonly struct DirectoryEntry 
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
        /// This corresponds exactly to the (internal) DirectoryArtifact field with the same name.
        /// </summary>
        public readonly uint IsSharedOpaquePlusPartialSealId;

        private const byte IsSharedOpaqueShift = 31;

        private const uint IsSharedOpaqueBit = 1U << IsSharedOpaqueShift;

        private const uint PartialSealIdMask = (1U << IsSharedOpaqueShift) - 1;

        /// <summary>
        /// Construct a DirectoryEntry.
        /// </summary>
        public DirectoryEntry(NameId path, PipId producerPip, ContentFlags contentFlags, bool isSharedOpaque, uint partialSealId)
        {
            Contract.Requires(!isSharedOpaque || (partialSealId > 0), "A shared opaque directory should always have a proper seal id");
            Contract.Requires((partialSealId & ~PartialSealIdMask) == 0, "The most significant bit of a partial seal id should not be used");

            Path = path;
            ProducerPip = producerPip;
            ContentFlags = contentFlags;
            IsSharedOpaquePlusPartialSealId = partialSealId | (isSharedOpaque ? IsSharedOpaqueBit : 0);
        }

        /// <summary>
        /// Construct a DirectoryEntry with an already-encoded partial seal field.
        /// </summary>
        public DirectoryEntry(NameId path, PipId producerPip, ContentFlags contentFlags, uint isSharedOpaquePlusPartialSealId)
        {
            Path = path;
            ProducerPip = producerPip;
            ContentFlags = contentFlags;
            IsSharedOpaquePlusPartialSealId = isSharedOpaquePlusPartialSealId;
        }

        /// <summary>
        /// Construct a DirectoryEntry with replaced content flags.
        /// </summary>
        public DirectoryEntry WithContentFlags(ContentFlags contentFlags) 
            => new DirectoryEntry(Path, ProducerPip, contentFlags, IsSharedOpaquePlusPartialSealId);

        /// <summary>
        /// The unique id for partially sealed directories
        /// </summary>
        public uint PartialSealId => IsSharedOpaquePlusPartialSealId & PartialSealIdMask;

        /// <summary>
        /// Whether this directory represents a shared opaque directory
        /// </summary>
        public bool IsSharedOpaque => (IsSharedOpaquePlusPartialSealId & IsSharedOpaqueBit) != 0;

        /// <nodoc />
        public struct EqualityComparer : IEqualityComparer<DirectoryEntry>
        {
            /// <nodoc />
            public bool Equals(DirectoryEntry x, DirectoryEntry y) 
                => x.Path.Equals(y.Path) 
                   && x.IsSharedOpaquePlusPartialSealId == y.IsSharedOpaquePlusPartialSealId;

            /// <nodoc />
            public int GetHashCode([DisallowNull] DirectoryEntry obj) 
                => obj.Path.GetHashCode() ^ obj.IsSharedOpaquePlusPartialSealId.GetHashCode();
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
        /// The pathnames of directories in this DirectoryTable.
        /// </summary>
        /// <remarks>
        /// This table is shared between this table and the FileTable.
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
            public DirectoryId GetOrAdd(
                string directoryPath, 
                PipId producerPip, 
                ContentFlags contentFlags, 
                bool isSharedOpaque, 
                uint partialSealId)
            {
                DirectoryEntry entry = new DirectoryEntry(
                    PathTableBuilder.GetOrAdd(directoryPath), 
                    producerPip, 
                    contentFlags, 
                    isSharedOpaque, 
                    partialSealId);
                return GetOrAdd(entry);
            }
        }
    }
}
