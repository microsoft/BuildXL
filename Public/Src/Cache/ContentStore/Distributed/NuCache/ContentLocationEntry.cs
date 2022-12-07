// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Location information for a piece of content.
    /// </summary>
    public sealed class ContentLocationEntry : IEquatable<ContentLocationEntry>
    {
        /// <nodoc />
        public const int BytesInFileSize = sizeof(long);

        /// <nodoc />
        public const int BitsInFileSize = BytesInFileSize * 8;

        private readonly UnixTime? _creationTimeUtc;

        /// <summary>
        /// True if the entry is a special "missing entry".
        /// </summary>
        public bool IsMissing => LastAccessTimeUtc == default && Locations.Count == 0;

        /// <summary>
        /// Returns a set of locations for a given content.
        /// </summary>
        public MachineIdSet Locations { get; }

        /// <summary>
        /// Content size in bytes.
        /// </summary>
        public long ContentSize { get; }

        /// <summary>
        /// Last access time for a current entry.
        /// </summary>
        /// <remarks>
        /// Unlike other properties of this type, this property is not obtained from the remote store.
        /// </remarks>
        public UnixTime LastAccessTimeUtc { get; }

        /// <summary>
        /// Returns time when the entry was created (if provided) or last access time otherwise.
        /// </summary>
        public UnixTime CreationTimeUtc => _creationTimeUtc ?? LastAccessTimeUtc;

        /// <nodoc />
        private ContentLocationEntry(MachineIdSet locations, long contentSize, UnixTime lastAccessTimeUtc, UnixTime? creationTimeUtc)
        {
            Contract.RequiresNotNull(locations);
            Locations = locations;
            ContentSize = contentSize;
            LastAccessTimeUtc = lastAccessTimeUtc;
            _creationTimeUtc = creationTimeUtc;
        }

        /// <summary>
        /// Factory method that creates a valid content location.
        /// </summary>
        public static ContentLocationEntry Create(MachineIdSet locations, long contentSize, UnixTime lastAccessTimeUtc, UnixTime? creationTimeUtc = null)
        {
            return new ContentLocationEntry(locations, contentSize, lastAccessTimeUtc, creationTimeUtc);
        }

        /// <summary>
        /// Returns a special "missing" entry.
        /// </summary>
        public static ContentLocationEntry Missing { get; } = new ContentLocationEntry(MachineIdSet.Empty, -1, default, default);
        
        /// <summary>
        /// Serializes an instance into a binary stream.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            writer.WriteCompact(ContentSize);
            Locations.Serialize(writer);
            writer.Write(CreationTimeUtc);
            long lastAccessTimeOffset = LastAccessTimeUtc.Value - CreationTimeUtc.Value;
            writer.WriteCompact(lastAccessTimeOffset);
        }

        /// <summary>
        /// Serializes an instance into a binary stream.
        /// </summary>
        public void Serialize(ref SpanWriter writer)
        {
            writer.WriteCompact(ContentSize);
            Locations.Serialize(ref writer);
            writer.Write(CreationTimeUtc);
            long lastAccessTimeOffset = LastAccessTimeUtc.Value - CreationTimeUtc.Value;
            writer.WriteCompact(lastAccessTimeOffset);
        }

        /// <summary>
        /// Builds an instance from a binary stream.
        /// </summary>
        public static ContentLocationEntry Deserialize(BuildXLReader reader)
        {
            var size = reader.ReadInt64Compact();
            var locations = MachineIdSet.Deserialize(reader);
            var creationTimeUtc = reader.ReadUnixTime();
            var lastAccessTimeOffset = reader.ReadInt64Compact();
            var lastAccessTime = new UnixTime(creationTimeUtc.Value + lastAccessTimeOffset);
            if (size == -1 && lastAccessTime == default)
            {
                return Missing;
            }

            return Create(locations, size, lastAccessTime, creationTimeUtc);
        }

        /// <summary>
        /// Builds an instance from a binary stream.
        /// </summary>
        public static ContentLocationEntry Deserialize(ReadOnlySpan<byte> input)
        {
            var reader = new SpanReader(input);
            return Deserialize(ref reader);
        }

        /// <summary>
        /// Builds an instance from a binary stream.
        /// </summary>
        public static ContentLocationEntry Deserialize(ref SpanReader reader)
        {
            var size = reader.ReadInt64Compact();
            var locations = MachineIdSet.Deserialize(ref reader);
            var (creationTime, lastAccessTime) = ReadCreationAndLastAccessTimes(ref reader);

            if (size == -1 && lastAccessTime == default)
            {
                return Missing;
            }

            return Create(locations, size, lastAccessTime, creationTime);
        }

        private static (UnixTime creationTime, UnixTime lastAccessTime) ReadCreationAndLastAccessTimes(ref SpanReader reader)
        {
            var creationTime = reader.ReadUnixTime();
            var lastAccessTimeOffset = reader.ReadInt64Compact();
            var lastAccessTimeUtc = new UnixTime(creationTime.Value + lastAccessTimeOffset);
            return (creationTime, lastAccessTimeUtc);
        }

        /// <summary>
        /// Process and merge two <see cref="ContentLocationEntry"/> into the <paramref name="mergeWriter"/>.
        /// </summary>
        public static bool TryMergeSortedLocations(ref SpanReader reader1, ref SpanReader reader2, ref SpanWriter mergeWriter)
        {
            var entry1Size = reader1.ReadInt64Compact();
            var entry2Size = reader2.ReadInt64Compact();

            // One of the entries is missing, falling back to regular merge.
            if (entry1Size == -1 || entry2Size == -1)
            {
                return false;
            }

            Contract.Assert(entry1Size == entry2Size, $"Sorted content location entries must have equal size. entry1Size={entry1Size}, entry2Size={entry2Size}.");

            if (!SortedLocationChangeMachineIdSet.CanMergeSortedLocations(ref reader1, ref reader2))
            {
                // Can't merge in-flight. Need to deserialize the entries and merge in memory.
                return false;
            }

            // Writing the Size first
            mergeWriter.WriteCompact(entry1Size);

            // Writing a list of merged locations.
            SortedLocationChangeMachineIdSet.MergeSortedMachineLocationChanges(ref reader1, ref reader2, ref mergeWriter);

            // Writing creation and last access times.
            var (entry1CreationTime, entry1LastAccessTime) = ReadCreationAndLastAccessTimes(ref reader1);
            var (entry2CreationTime, entry2LastAccessTime) = ReadCreationAndLastAccessTimes(ref reader2);

            var maxCreationTime = UnixTime.Min(entry1CreationTime, entry2CreationTime);
            var maxLastAccessTimeOffset = UnixTime.Max(entry1LastAccessTime, entry2LastAccessTime).Value - maxCreationTime.Value;

            mergeWriter.Write(maxCreationTime);
            mergeWriter.WriteCompact(maxLastAccessTimeOffset);
            return true;
        }

        /// <nodoc />
        public ContentLocationEntry SetMachineExistence(in MachineIdCollection machines, bool exists, UnixTime? lastAccessTime = null, long? size = null)
        {
            var locations = Locations.SetExistence(machines, exists);
            if ((lastAccessTime == null || lastAccessTime.Value.Value <= LastAccessTimeUtc.Value)
                && locations.Count == Locations.Count
                && (size == null || ContentSize >= 0))
            {
                return this;
            }

            return new ContentLocationEntry(locations, size ?? ContentSize, lastAccessTime ?? LastAccessTimeUtc, CreationTimeUtc);
        }

        /// <nodoc />
        public ContentLocationEntry Merge(ContentLocationEntry other, bool sortLocations) => MergeEntries(this, other, sortLocations);

        /// <nodoc />
        public static ContentLocationEntry MergeEntries(ContentLocationEntry entry1, ContentLocationEntry entry2, bool sortLocations)
        {
            if (entry1 == null || entry1.IsMissing)
            {
                return entry2;
            }

            if (entry2 == null || entry2.IsMissing)
            {
                return entry1;
            }

            return new ContentLocationEntry(
                entry1.Locations.Merge(entry2.Locations, sortLocations),
                entry1.ContentSize,
                UnixTime.Max(entry1.LastAccessTimeUtc, entry2.LastAccessTimeUtc),
                UnixTime.Min(entry1.CreationTimeUtc, entry2.CreationTimeUtc));
        }
        
        /// <nodoc />
        public ContentLocationEntry Touch(UnixTime accessTime)
        {
            return new ContentLocationEntry(Locations, ContentSize, accessTime > LastAccessTimeUtc ? accessTime : LastAccessTimeUtc, CreationTimeUtc);
        }
        
        /// <inheritdoc />
        public override string ToString()
        {
            return IsMissing ? "Missing location" : $"Size: {ContentSize}b*{Locations.Count}, Created: {CreationTimeUtc}, Accessed at: {LastAccessTimeUtc}";
        }

        /// <inheritdoc />
        public bool Equals(ContentLocationEntry other)
        {
            if (other is null)
            {
                return false;
            }

            return _creationTimeUtc == other._creationTimeUtc
                   && Locations.Equals(other.Locations)
                   && ContentSize == other.ContentSize
                   && LastAccessTimeUtc == other.LastAccessTimeUtc;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return ReferenceEquals(this, obj) || obj is ContentLocationEntry other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (_creationTimeUtc.GetHashCode(), Locations?.GetHashCode() ?? 0, ContentSize.GetHashCode(), LastAccessTimeUtc.GetHashCode()).GetHashCode();
        }
    }
}
