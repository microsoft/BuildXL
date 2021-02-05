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
        protected DedupIdentifier(byte[] algorithmResult, byte algorithmId)
        {
            Contract.Requires(algorithmResult != null);
            Contract.Requires(algorithmResult.Length == 32);

            _value = new byte[algorithmResult.Length + 1];
            algorithmResult.CopyTo(_value, 0);
            _value[algorithmResult.Length] = algorithmId;
        }

        protected DedupIdentifier(HashAndAlgorithm hashAndAlgorithm)
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
            return DedupIdentifier.Create(new HashAndAlgorithm(value));
        }

        public static DedupIdentifier Create(DedupNode node)
        {
            Contract.Requires(node != null);

            return Create(
                node.Hash,
                (node.Type == DedupNode.NodeType.ChunkLeaf) ?
                    ChunkDedupIdentifier.ChunkAlgorithmId :
                    (byte)NodeAlgorithmId.Node64K); // TODO: We need to fix this.
        }

        public static DedupIdentifier Create(byte[] algorithmResult, byte algorithmId)
        {
            Contract.Requires(algorithmResult != null);

            if (algorithmId == ChunkDedupIdentifier.ChunkAlgorithmId)
            {
                return new ChunkDedupIdentifier(algorithmResult);
            }
            else if (((NodeAlgorithmId)algorithmId).IsValidNode())
            {
                return new NodeDedupIdentifier(algorithmResult, (NodeAlgorithmId)algorithmId);
            }

            throw new NotSupportedException($"Unknown algorithm {algorithmId}");
        }

        public static DedupIdentifier Create(HashAndAlgorithm hashAndAlgorithm)
        {
            Contract.Requires(hashAndAlgorithm.Bytes != null);

            byte algorithmId = hashAndAlgorithm.AlgorithmId;
            if (algorithmId == ChunkDedupIdentifier.ChunkAlgorithmId)
            {
                return new ChunkDedupIdentifier(hashAndAlgorithm);
            }
            else if (((NodeAlgorithmId)algorithmId).IsValidNode())
            {
                return new NodeDedupIdentifier(hashAndAlgorithm);
            }
            else
            {
                throw new NotSupportedException($"Unknown algorithm {algorithmId}");
            }
        }

        public NodeDedupIdentifier CastToNodeDedupIdentifier()
        {
            return new NodeDedupIdentifier(new HashAndAlgorithm(Value));
        }

        public ChunkDedupIdentifier CastToChunkDedupIdentifier()
        {
            return new ChunkDedupIdentifier(new HashAndAlgorithm(Value));
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
        /// Hash produced by AlgorithmId's hashing algorithm.
        /// </summary>
        public byte[] AlgorithmResult => _value.Take(AlgorithmIdIndex).ToArray();

        /// <summary>
        /// Dedup identifier string representation without the algorithm Id.
        /// </summary>
        public string AlgorithmResultString => AlgorithmResult.ToHex();

        /// <summary>
        /// Dedup identifier string representation with the algorithm Id.
        /// </summary>
        public string ValueString => _value.ToHex();

        /// <summary>
        /// Byte appended to end of identifier to mark the type of hashing algorithm used.
        /// </summary>
        public byte AlgorithmId => _value[AlgorithmIdIndex];

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

    public readonly struct HashAndAlgorithm
    {
        public readonly byte[] Bytes;
        public byte AlgorithmId => Bytes[Bytes.Length - 1];
        public HashAndAlgorithm(byte[] bytes)
        {
            Contract.Requires(bytes != null);
            Contract.Check(bytes.Length > 32)?.Assert($"Byte representing the hash algorithm id is missing. Actual Hash Length: {bytes.Length}");
            Bytes = bytes;
        }
    }
}
