// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

#pragma warning disable SA1600 // Elements must be documented

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <nodoc />
    public sealed class ChunkDedupIdentifier : DedupIdentifier
    {
        /// <nodoc />
        public const byte ChunkAlgorithmId = 1;

        /// <nodoc />
        public ChunkDedupIdentifier(byte[] hashResult)
            : base(hashResult, ChunkAlgorithmId)
        {
            if (AlgorithmId != ChunkAlgorithmId)
            {
                throw new ArgumentException($"The given hash does not represent a {nameof(ChunkDedupIdentifier)}");
            }
        }
    }

    /// <nodoc />
    public sealed class NodeDedupIdentifier : DedupIdentifier
    {
        /// <nodoc />
        public NodeDedupIdentifier(byte[] hashResult, byte algorithmId)
            : base(hashResult, algorithmId)
        {
            if (((NodeAlgorithmId)AlgorithmId).IsValidNode())
            {
                throw new ArgumentException($"The given hash does not represent a {nameof(NodeDedupIdentifier)}");
            }
        }
    }

    /// <summary>
    /// Duplicated relevant sections from Artifact code to avoid introducing dependencies.
    /// Follows the current pattern of BlobIdentifier, which exists in both repos.
    /// Original: "http://index/?leftProject=Microsoft.VisualStudio.Services.BlobStore.Common&amp;file=DedupIdentifier.cs"
    /// </summary>
    public abstract class DedupIdentifier : IEquatable<DedupIdentifier>, IComparable<DedupIdentifier>
    {
        private readonly byte[] _value;

        /// <nodoc />
        protected DedupIdentifier(byte[] algorithmResult, byte algorithmId)
        {
            Contract.Requires(algorithmResult != null);
            Contract.Requires(algorithmResult.Length == 32);

            _value = new byte[algorithmResult.Length + 1];
            algorithmResult.CopyTo(_value, 0);
            _value[algorithmResult.Length] = algorithmId;
        }

        /// <nodoc />
        public BlobIdentifier ToBlobIdentifier() => new BlobIdentifier(AlgorithmResult, AlgorithmId);

        /// <summary>
        /// Hash produced by AlgorithmId's hashing algorithm.
        /// </summary>
        public byte[] AlgorithmResult => _value.Take(AlgorithmIdIndex).ToArray();

        /// <summary>
        /// Byte appended to end of identifier to mark the type of hashing algorithm used.
        /// </summary>
        public byte AlgorithmId => _value[AlgorithmIdIndex];

        private int AlgorithmIdIndex => _value.Length - 1;

        /// <nodoc />
        public bool Equals(DedupIdentifier? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return (other != null) && _value.SequenceEqual(other._value);
        }

        /// <nodoc />
        public int CompareTo(DedupIdentifier? other)
        {
            if (other == null)
            {
                return -1;
            }

            return ByteArrayComparer.Instance.Compare(_value, other._value);
        }
    }
}
