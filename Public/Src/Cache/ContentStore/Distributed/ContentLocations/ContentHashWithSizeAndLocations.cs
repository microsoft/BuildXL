// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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
        /// The content hash for the specified locations.
        /// </summary>
        public ContentHash ContentHash { get; }

        /// <summary>
        /// The size of the content hash's file.
        /// </summary>
        public long Size { get; }

        /// <nodoc />
        public ContentHashWithSizeAndLocations(ContentHash contentHash, long size = -1)
        {
            ContentHash = contentHash;
            Size = size;
        }

        /// <nodoc />
        public ContentHashWithSizeAndLocations(ContentHash contentHash, long size, IReadOnlyList<MachineLocation> locations)
        {
            ContentHash = contentHash;
            Size = size;
            Locations = locations;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[ContentHash={ContentHash.ToShortString()} Size={Size} LocationCount={Locations?.Count}]";
        }

        /// <inheritdoc />
        public bool Equals(ContentHashWithSizeAndLocations other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Locations.SequenceEqual(other.Locations) && ContentHash.Equals(other.ContentHash) && Size == other.Size;
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
    }
}
