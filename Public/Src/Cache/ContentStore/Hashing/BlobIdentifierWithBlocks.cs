// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

// disable 'Missing XML comment for publicly visible type' warnings.
#pragma warning disable 1591
#pragma warning disable SA1600 // Elements must be documented

namespace BuildXL.Cache.ContentStore.Hashing
{
    public sealed class BlobIdentifierWithBlocks : IEquatable<BlobIdentifierWithBlocks>
    {
        /// <summary>
        /// Gets the globally unique identities of each chunk in this blob
        /// </summary>
        public IList<BlobBlockHash> BlockHashes { get; }

        public BlobIdentifier BlobId { get; }

        private static readonly char[] SplitCharacters = { ',' };

        public BlobIdentifierWithBlocks(BlobIdentifier blobId, IEnumerable<BlobBlockHash> blockIdentifiers)
        {
            BlockHashes = blockIdentifiers.ToList();
            BlobId = blobId;
            Validate();
        }

        private static BlobIdentifier ComputeIdentifierBasedOnBlocks(IEnumerable<BlobBlockHash> blocks)
        {
            var rollingId = new VsoHash.RollingBlobIdentifier();

            // ReSharper disable once GenericEnumeratorNotDisposed
            IEnumerator<BlobBlockHash> enumerator = blocks.GetEnumerator();

            bool isLast = !enumerator.MoveNext();
            if (isLast)
            {
                throw new InvalidDataException("Blob must have at least one block.");
            }

            BlobBlockHash current = enumerator.Current;
            isLast = !enumerator.MoveNext();
            while (!isLast)
            {
                rollingId.Update(current);
                current = enumerator.Current;
                isLast = !enumerator.MoveNext();
            }

            return rollingId.Finalize(current);
        }

        public static BlobIdentifierWithBlocks Deserialize(string serialized)
        {
            // Marked "new"
            string[] tokens = serialized.Split(':');
            return new BlobIdentifierWithBlocks(
                BlobIdentifier.Deserialize(tokens[0]),
                tokens[1].Split(SplitCharacters, StringSplitOptions.RemoveEmptyEntries).Select(idString => new BlobBlockHash(idString)).ToList());
        }

        public string Serialize()
        {
            return $"{BlobId.ValueString}:{string.Join(",", BlockHashes.Select(id => id.HashString))}";
        }

        private void Validate()
        {
            if (BlobId == null)
            {
                throw new ArgumentNullException(nameof(BlobId));
            }

            if (BlockHashes == null)
            {
                throw new ArgumentNullException(nameof(BlockHashes));
            }

            BlobIdentifier computedBlobId = ComputeIdentifierBasedOnBlocks(BlockHashes);
            if (!BlobId.Equals(computedBlobId))
            {
                throw new InvalidDataException(string.Format(
                    CultureInfo.InvariantCulture,
                    "Computed id '{0}' does not match given one '{1}'.",
                    computedBlobId,
                    BlobId));
            }
        }

        /// <summary>
        /// Equality is based on the BlobId and the type.
        /// </summary>
        public override bool Equals(object obj)
        {
            var other = obj as BlobIdentifierWithBlocks;
            return other != null && Equals(other);
        }

        /// <summary>
        /// Equality is based on the BlobId and the type.
        /// </summary>
        public bool Equals(BlobIdentifierWithBlocks other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return (other != null) && BlobId.Equals(other.BlobId);
        }

        /// <summary>
        /// The hash is computed from the BlobIdand tagged with the type to
        /// distiguish it from the respective BlobIdentifier.
        /// </summary>
        public override int GetHashCode()
        {
            return EqualityHelper.GetCombinedHashCode(BlobId, GetType());
        }

        /// <summary>
        /// Returns a user-friendly, non-canonical string representation of the unique identifier for binary content
        /// </summary>
        /// <returns>
        /// A user-friendly, non-canonical string representation of the content identifier
        /// </returns>
        public override string ToString()
        {
            return $"BlobWithBlocks:{Serialize()}";
        }

        public static bool operator ==(BlobIdentifierWithBlocks left, BlobIdentifierWithBlocks right)
        {
            if (ReferenceEquals(left, null))
            {
                return ReferenceEquals(right, null);
            }

            return left.Equals(right);
        }

        public static bool operator !=(BlobIdentifierWithBlocks left, BlobIdentifierWithBlocks right)
        {
            return !(left == right);
        }
    }
}
