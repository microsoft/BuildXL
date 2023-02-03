using System;
using System.Diagnostics.ContractsLight;

#pragma warning disable SA1600 // Elements must be documented

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <nodoc />
    public sealed class NodeDedupIdentifier : DedupIdentifier
    {
        internal static readonly IContentHasher Hasher = DedupSingleChunkHashInfo.Instance.CreateContentHasher(); // Regardless of underlying chunk size, always hash this way.

        /// <nodoc />
        public NodeDedupIdentifier(HashAndAlgorithmId hash)
            : base(hash)
        {
            Contract.Requires(hash.Bytes != null);
            Contract.Assert(hash.AlgorithmId == Hashing.AlgorithmId.Node, $"The given hash does not represent a {nameof(NodeDedupIdentifier)}: {hash.AlgorithmId}");
        }

        /// <nodoc />
        public NodeDedupIdentifier(byte[] hashResult)
            : base(hashResult, Hashing.AlgorithmId.Node)
        {
            Contract.Requires(hashResult != null);
        }

        /// <nodoc />
        public static NodeDedupIdentifier CalculateIdentifierFromSerializedNode(byte[] bytes)
        {
            Contract.Requires(bytes != null);
            return new NodeDedupIdentifier(Hasher.GetContentHash(bytes).ToHashByteArray());
        }

        /// <nodoc />
        public static NodeDedupIdentifier CalculateIdentifierFromSerializedNode(byte[] bytes, int offset, int count)
        {
            Contract.Requires(bytes != null);
            return new NodeDedupIdentifier(Hasher.GetContentHash(bytes, offset, count).ToHashByteArray());
        }

        /// <nodoc />
        public static NodeDedupIdentifier CalculateIdentifierFromSerializedNode(ArraySegment<byte> bytes)
        {
            Contract.Requires(bytes.Array != null);
            return new NodeDedupIdentifier(Hasher.GetContentHash(bytes.Array, bytes.Offset, bytes.Count).ToHashByteArray());
        }

        /// <nodoc />
        public static NodeDedupIdentifier Parse(string value)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(value));
            return (NodeDedupIdentifier)Create(value);
        }
    }
}
