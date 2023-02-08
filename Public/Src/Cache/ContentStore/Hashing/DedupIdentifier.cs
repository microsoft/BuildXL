// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using System.ComponentModel;

#pragma warning disable SA1600 // Elements must be documented
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace BuildXL.Cache.ContentStore.Hashing
{
    [TypeConverter(typeof(DedupIdentifierTypeConverter))]
    public abstract class DedupIdentifier : IEquatable<DedupIdentifier>, IComparable<DedupIdentifier>, ILongHash
    {
        private readonly byte[] _value;
        private int AlgorithmIdIndex => _value.Length - 1;
        /// <nodoc />
        protected DedupIdentifier(byte[] algorithmResult,AlgorithmId dedupType)
        {
            Contract.Requires(algorithmResult != null);
            Contract.Requires(algorithmResult.Length == 32);

            _value = new byte[algorithmResult.Length + 1];
            algorithmResult.CopyTo(_value, 0);

            _value[algorithmResult.Length] = (byte)dedupType;
        }

        protected DedupIdentifier(HashAndAlgorithmId hashAndAlgorithm)
        {
            if (null == hashAndAlgorithm.Bytes)
            {
                throw new ArgumentNullException(nameof(hashAndAlgorithm));
            }

            _value = hashAndAlgorithm.Bytes;
        }

        public static DedupIdentifier Create(string valueIncludingAlgorithm)
        {
            if (string.IsNullOrWhiteSpace(valueIncludingAlgorithm))
            {
                throw new ArgumentNullException(nameof(valueIncludingAlgorithm));
            }

            byte[] value = HexUtilities.HexToBytes(valueIncludingAlgorithm);
            return DedupIdentifier.Create(new HashAndAlgorithmId(value));
        }

        public static DedupIdentifier Create(DedupNode node)
        {
            Contract.Requires(node != null);

            return Create(
                node.Hash,
                node.Type == DedupNode.NodeType.ChunkLeaf ? Hashing.AlgorithmId.Chunk 
                    : Hashing.AlgorithmId.Node); 
        }

        public static DedupIdentifier Create(byte[] algorithmResult, AlgorithmId dedupType)
        {
            Contract.Requires(algorithmResult != null);

            if (dedupType == Hashing.AlgorithmId.Chunk)
            {
                return new ChunkDedupIdentifier(algorithmResult);
            }
            else if (dedupType == Hashing.AlgorithmId.Node)
            {
                return new NodeDedupIdentifier(algorithmResult);
            }

            throw new NotSupportedException($"Unknown algorithm {dedupType}");
        }

        public static DedupIdentifier Create(HashAndAlgorithmId hashAndAlgorithm)
        {
            Contract.Requires(hashAndAlgorithm.Bytes != null);

            AlgorithmId dedupType = hashAndAlgorithm.AlgorithmId;
            if (dedupType == Hashing.AlgorithmId.Chunk)
            {
                return new ChunkDedupIdentifier(hashAndAlgorithm);
            }
            else if (dedupType == Hashing.AlgorithmId.Node)
            {
                return new NodeDedupIdentifier(hashAndAlgorithm);
            }
            else
            {
                throw new NotSupportedException($"Unknown algorithm {dedupType}");
            }
        }

        public NodeDedupIdentifier CastToNodeDedupIdentifier()
        {
            return new NodeDedupIdentifier(new HashAndAlgorithmId(Value));
        }

        public ChunkDedupIdentifier CastToChunkDedupIdentifier()
        {
            return new ChunkDedupIdentifier(new HashAndAlgorithmId(Value));
        }

        /// <summary>
        /// Create a dedup identifier from a string.
        /// </summary>
        /// <remarks>
        /// <pre>
        /// This method has two purposes:
        /// 1) Create is overloaded, so any library referencing Create() must be able to resolve all the
        ///    parameter types across all the overloaded versions, which include
        ///    BuildXL.Cache.ContentStore.Hashing.DedupNode. This method can help reduce compile-time dependency.
        /// 2) To add some API consistency, it has the same signature as BlobIdentifier.Deserialize(string).
        /// </pre>
        /// </remarks>
        public static DedupIdentifier Deserialize(string valueIncludingAlgorithm)
        {
            return Create(valueIncludingAlgorithm);
        }

        /// <nodoc />
        public BlobIdentifier ToBlobIdentifier() => new BlobIdentifier(AlgorithmResult, AlgorithmId);

        /// <summary>
        /// Hash produced by Dedup Types's hashing algorithm.
        /// </summary>
        public byte[] AlgorithmResult => _value.Take(AlgorithmIdIndex).ToArray();

        /// <summary>
        /// Dedup identifier string representation without the dedup type.
        /// </summary>
        public string AlgorithmResultString => AlgorithmResult.ToHex();

        /// <summary>
        /// Dedup identifier string representation with thededup type.
        /// </summary>
        public string ValueString => _value.ToHex();

        /// <summary>
        /// Get the type of dedup represented by this content:
        /// </summary>
        public AlgorithmId AlgorithmId => (AlgorithmId)_value[AlgorithmIdIndex];

        /// <nodoc />
        public byte[] Value
        {
            get
            {
                byte[] copy = new byte[_value.Length];
                _value.CopyTo(copy, 0);
                return copy;
            }
        }

        public bool Equals(DedupIdentifier? other)
        {
            if (object.ReferenceEquals(other, null))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return _value.SequenceEqual(other._value);
        }

         /// <inheritdoc/>
        public override bool Equals(Object? obj) => Equals(obj as DedupIdentifier);

        public override string ToString()
        {
            return ValueString;
        }

        public override int GetHashCode()
        {
            return BitConverter.ToInt32(_value, 0);
        }

        public long GetLongHashCode()
        {
            return BitConverter.ToInt64(_value, 0);
        }

        public static bool operator ==(DedupIdentifier? x, DedupIdentifier? y)
        {
            if (ReferenceEquals(x, null))
            {
                return ReferenceEquals(y, null);
            }

            return x.Equals(y);
        }

        public static bool operator !=(DedupIdentifier? x, DedupIdentifier? y)
        {
            return !(x == y);
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

    public readonly struct HashAndAlgorithmId
    {
        public readonly byte[] Bytes;
        public AlgorithmId AlgorithmId => (Hashing.AlgorithmId)Bytes[Bytes.Length - 1];
        public HashAndAlgorithmId(byte[] bytes)
        {
            Contract.Requires(bytes != null);
            Contract.Assert(bytes.Length > 32, $"Byte representing the hash algorithm id is missing. Actual Hash Length: {bytes.Length}");
            Bytes = bytes;
        }
    }
}
