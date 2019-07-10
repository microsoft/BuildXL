// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Hashing;

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
        public readonly IReadOnlyList<MachineLocation> Locations;

        /// <summary>
        /// The content hash for the specified locations.
        /// </summary>
        public readonly ContentHash ContentHash;

        /// <summary>
        /// The size of the content hash's file.
        /// </summary>
        public readonly long Size;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentHashWithSizeAndLocations"/> class.
        /// </summary>
        public ContentHashWithSizeAndLocations(ContentHash contentHash, long size = -1, IReadOnlyList<MachineLocation> locations = null)
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
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Locations.SequenceEqual(other.Locations) == true && ContentHash.Equals(other.ContentHash) && Size == other.Size;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
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
            unchecked
            {
                var hashCode = Locations.SequenceHashCode();
                hashCode = (hashCode * 397) ^ ContentHash.GetHashCode();
                hashCode = (hashCode * 397) ^ Size.GetHashCode();
                return hashCode;
            }
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
