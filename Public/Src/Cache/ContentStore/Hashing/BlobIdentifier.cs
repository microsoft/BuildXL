// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

// disable 'Missing XML comment for publicly visible type' warnings.
#pragma warning disable 1591
#pragma warning disable SA1600 // Elements must be documented

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Represents a hash identifier for content stored in the Content Repository.
    /// Internally represented as a byte array of the algorithm result with a single byte algorithm identifier appended.
    /// </summary>
    [Serializable]
    public sealed class BlobIdentifier : IEquatable<BlobIdentifier>, IComparable
    {
        private const int MinimumIdentifierValueByteCount = 4;
        private const int MinimumAlgorithmResultByteCount = MinimumIdentifierValueByteCount - 1;
        private readonly byte[] _identifierValue;

        public BlobIdentifier(byte[] algorithmResult, byte algorithmId)
        {
            Contract.Requires(algorithmResult != null);

            // copy algorithmResult and append AlgorithmId to identifierValue
            _identifierValue = new byte[algorithmResult.Length + 1];
            algorithmResult.CopyTo(_identifierValue, 0);
            _identifierValue[algorithmResult.Length] = algorithmId;
            Validate();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobIdentifier"/> class.
        /// </summary>
        /// <remarks>
        /// The value is expected to contain both the hash and the algorithm id.
        /// </remarks>
        /// <param name="valueIncludingAlgorithm">Must be the value corresponding to the ValueString of the id to be created.</param>
        private BlobIdentifier(string valueIncludingAlgorithm)
        {
            if (string.IsNullOrWhiteSpace(valueIncludingAlgorithm))
            {
                throw new ArgumentNullException(nameof(valueIncludingAlgorithm), "Invalid hash value");
            }

            // Ignore the result of this call as ValidateInternal will check for null.
            _identifierValue = HexUtilities.HexToBytes(valueIncludingAlgorithm);
            Validate();
        }

        /// <summary>
        /// Gets the unique identifier for binary content computed when the
        /// class instance was created  (ex:  54CE418A2A89A74B42CC396301).
        /// This is the complete value as it includes the AlgorithmId suffix.
        /// </summary>
        public string ValueString => _identifierValue.ToHex();

        /// <summary>
        /// Gets a copy of byte array underlying this identifier.
        /// </summary>
        public byte[] Bytes
        {
            get
            {
                byte[] copy = new byte[_identifierValue.Length];
                _identifierValue.CopyTo(copy, 0);
                return copy;
            }
        }

        /// <summary>
        /// Gets the (single byte) algorithm id used to generate the blob identifier (hash).
        /// </summary>
        public byte AlgorithmId => _identifierValue[AlgorithmIdIndex];

        private int AlgorithmIdIndex => _identifierValue.Length - 1;

        public static BlobIdentifier CreateFromAlgorithmResult(string algorithmResult, byte algorithmId = VsoHash.VsoAlgorithmId)
        {
            return new BlobIdentifier(HexUtilities.HexToBytes(algorithmResult), algorithmId);
        }

        public static BlobIdentifier Deserialize(string valueIncludingAlgorithm)
        {
            return new BlobIdentifier(valueIncludingAlgorithm);
        }

        public static bool operator ==(BlobIdentifier left, BlobIdentifier right)
        {
            if (ReferenceEquals(left, null))
            {
                return ReferenceEquals(right, null);
            }

            return left.Equals(right);
        }

        public static bool operator !=(BlobIdentifier left, BlobIdentifier right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Returns true/false whether the object is equal to the current <see cref="BlobIdentifier"/>
        /// </summary>
        /// <param name="obj">The object to compare against the current instance</param>
        /// <returns>
        /// <c>true</c> if the objects are equal, otherwise <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            var other = obj as BlobIdentifier;
            return other != null && Equals(other);
        }

        /// <summary>
        /// Returns true/false whether the <see cref="BlobIdentifier"/> is equal to the current <see cref="BlobIdentifier"/>
        /// </summary>
        /// <param name="other">The <see cref="BlobIdentifier"/> to compare against the current instance</param>
        /// <returns>
        /// <c>true</c> if the objects are equal, otherwise <c>false</c>.
        /// </returns>
        public bool Equals(BlobIdentifier other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return (other != null) && _identifierValue.SequenceEqual(other._identifierValue);
        }

        /// <summary>
        /// Gets the unique hash for this unique identifier for binary content.
        /// </summary>
        /// <returns>
        /// A hash value for the content identifier
        /// </returns>
        public override int GetHashCode()
        {
            return BitConverter.ToInt32(_identifierValue, 0);
        }

        /// <summary>
        /// Returns a user-friendly, non-canonical string representation of the unique identifier for binary content
        /// </summary>
        /// <returns>
        /// A user-friendly, non-canonical string representation of the content identifier
        /// </returns>
        public override string ToString()
        {
            return $"Blob:{ValueString}";
        }

        public int CompareTo(object obj)
        {
            if (!(obj is BlobIdentifier))
            {
                throw new ArgumentException("Object is not a BlobIdentifier");
            }

            return CompareTo((BlobIdentifier)obj);
        }

        private int CompareTo(BlobIdentifier other)
        {
            return other == null ? 1 : string.Compare(ValueString, other.ValueString, StringComparison.InvariantCultureIgnoreCase);
        }

        private void Validate()
        {
            Contract.Requires(_identifierValue != null);

            int algorithmResultLength = _identifierValue.Length - 1;

            // The final byte array needs to be at least 4 bytes long for GetHashCode to work.
            // We prevent ourselves from accidentally passing the wrong byte array (with the algorithm id instead of without
            // or vice versa) by requiring that all algorithm results have an even length.  Since the given string should
            // have the algorithm id, it should be an odd number of bytes.
            if ((algorithmResultLength < MinimumAlgorithmResultByteCount) || (algorithmResultLength % 2 != 0))
            {
                throw new ArgumentException("Invalid hash length", nameof(_identifierValue));
            }
        }
    }
}
