// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Location information for a piece of content.
    /// </summary>
    public sealed class ContentLocationEntry : IEquatable<ContentLocationEntry>
    {
        private static readonly Tracer Tracer = new Tracer(nameof(ContentLocationEntry));

        /// <summary>
        /// A size of a missing entry.
        /// </summary>
        public const long MissingSize = -1;

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
        public static ContentLocationEntry Missing { get; } = new ContentLocationEntry(MachineIdSet.Empty, contentSize: MissingSize, default, default);

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
        public static bool TryMergeSortedLocations(OperationContext context, ref SpanReader reader1, ref SpanReader reader2, ref SpanWriter mergeWriter)
        {
            var entry1Size = reader1.ReadInt64Compact();
            var entry2Size = reader2.ReadInt64Compact();

            // With the existing serialization scheme it is not easy to get if the entry is missing.
            // The missing entry is identified by a negative size (-1) AND the last access time is 'default'.
            // It is a little bit unfortunate because we can't make a decision right here.

            if (entry1Size != MissingSize && entry2Size != MissingSize && entry1Size != entry2Size)
            {
                // The size exists but they don't match.
                // Tracing this as a warning and falling back to the old merge logic.
                
                Tracer.Warning(context, $"Sorted content location entries must have equal size. entry1Size={entry1Size}, entry2Size={entry2Size}.");
                return false;
            }

            if (!SortedLocationChangeMachineIdSet.CanMergeSortedLocations(ref reader1, ref reader2))
            {
                // Can't merge in-flight. Need to deserialize the entries and merge in memory.
                // This case covers the Missing case.
                // If one of the entries is missing, then the MachineIdSet is not sorted, so we'll fallback to the serialization-based merge which should not be very common.
                return false;
            }

            // We could merge two entries with -1 size, like two removals.
            // In this case the merged size also would be -1.
            var entrySize = entry1Size > 0 ? entry1Size : entry2Size;

            // Writing the Size first
            mergeWriter.WriteCompact(entrySize);

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
