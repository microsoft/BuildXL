// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// A wrapper for content hashes and their respective size and locations.
    /// </summary>
    public class ContentHashWithSizeAndLocations : IEquatable<ContentHashWithSizeAndLocations>
    {
        /// <summary>
        /// Locations where the content hash will be found. A null list means the only location is the local machine and a populated list holds remote locations.
        /// </summary>
        /// TODO: change the comment. It is stale. (bug 1365340)
        /// <remarks>Can be null only for non-LLS case.</remarks>
        public IReadOnlyList<MachineLocation>? Locations { get; }

        /// <summary>
        /// A list of machines that used to have the content but they're currently unavailable.
        /// </summary>
        /// <remarks>
        /// Used for tracing purposes only.
        /// </remarks>
        public IReadOnlyList<MachineLocation>? FilteredOutInactiveMachineLocations { get; }

        /// <summary>
        /// Gets the number of extra locations that we added to the current instance by merging two lists. Used for tracing only.
        /// </summary>
        public int ExtraMergedLocations { get; init; }

        /// <summary>
        /// The content hash for the specified locations.
        /// </summary>
        public ContentHash ContentHash { get; }

        /// <summary>
        /// The size of the content hash's file.
        /// </summary>
        public long Size { get; }

        /// <summary>
        /// The size of the content hash's file.
        /// </summary>
        public long? NullableSize => Size == -1 ? null : Size;

        /// <summary>
        /// Optional underlying entry
        /// </summary>
        public ContentLocationEntry? Entry { get; }

        /// <summary>
        /// The content location origin
        /// </summary>
        public GetBulkOrigin? Origin { get; set; }

        /// <nodoc />
        public ContentHashWithSizeAndLocations(ContentHash contentHash, long size = -1)
        {
            ContentHash = contentHash;
            Size = size;
        }

        /// <nodoc />
        public ContentHashWithSizeAndLocations(
            ContentHash contentHash,
            long size,
            IReadOnlyList<MachineLocation> locations,
            ContentLocationEntry? entry = null,
            IReadOnlyList<MachineLocation>? filteredOutLocations = null,
            GetBulkOrigin? origin = null)
        {
            ContentHash = contentHash;
            Size = size;
            Locations = locations;
            Entry = entry;
            FilteredOutInactiveMachineLocations = filteredOutLocations;
            Origin = origin;
        }

        /// <summary>
        /// Merge two instances
        /// </summary>
        public static ContentHashWithSizeAndLocations Merge(ContentHashWithSizeAndLocations left, ContentHashWithSizeAndLocations right)
        {
            Contract.Requires(left.ContentHash == right.ContentHash);
            Contract.Requires(left.Size == -1 || right.Size == -1 || right.Size == left.Size);
            var finalList = (left.Locations ?? Enumerable.Empty<MachineLocation>()).Union(right.Locations ?? Enumerable.Empty<MachineLocation>()).ToList();

            return new ContentHashWithSizeAndLocations(
                left.ContentHash,
                Math.Max(left.Size, right.Size),
                finalList,
                // It doesn't matter if we sort locations or not here, because we're not going to store the results in RocksDb.
                ContentLocationEntry.MergeEntries(left.Entry, right.Entry, sortLocations: false),
                origin: left.Origin ?? right.Origin)
                   {
                        ExtraMergedLocations = finalList.Count - (left.Locations?.Count ?? 0)
                   };
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[ContentHash={ContentHash.ToShortString()} Size={Size} LocationCount={Locations?.Count}]";
        }

        /// <inheritdoc />
        public bool Equals([AllowNull]ContentHashWithSizeAndLocations other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Locations!.SequenceEqual(other.Locations!) && ContentHash.Equals(other.ContentHash) && Size == other.Size;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            if (obj is null)
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((ContentHashWithSizeAndLocations) obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (Locations.SequenceHashCode(), ContentHash, Size).GetHashCode();
        }

        /// <nodoc />
        public static bool operator ==(ContentHashWithSizeAndLocations left, ContentHashWithSizeAndLocations right)
        {
            return Equals(left, right);
        }

        /// <nodoc />
        public static bool operator !=(ContentHashWithSizeAndLocations left, ContentHashWithSizeAndLocations right)
        {
            return !Equals(left, right);
        }

        /// <nodoc />
        public static implicit operator ContentHashWithSize(ContentHashWithSizeAndLocations hashWithSizeAndLocations)
        {
            return new ContentHashWithSize(hashWithSizeAndLocations.ContentHash, hashWithSizeAndLocations.Size);
        }

        /// <nodoc />
        public static implicit operator ContentHash(ContentHashWithSizeAndLocations hashWithSizeAndLocations)
        {
            return hashWithSizeAndLocations.ContentHash;
        }
    }
}
