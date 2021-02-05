using System;
using System.Diagnostics.ContractsLight;

#pragma warning disable SA1600 // Elements must be documented

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <nodoc />
    public sealed class NodeDedupIdentifier : DedupIdentifier
    {
        private static readonly IContentHasher Hasher = DedupSingleChunkHashInfo.Instance.CreateContentHasher(); // Regardless of underlying chunk size, always hash this way.

        /// <nodoc />
        public byte NodeAlgorithm { get; } = (byte)NodeAlgorithmId.Node64K; // Default to node of 64K chunks.

        /// <nodoc />
        public NodeDedupIdentifier(HashAndAlgorithm hash)
            : base(hash)
        {
            Contract.Requires(hash.Bytes != null);
            Contract.Check(((NodeAlgorithmId)hash.AlgorithmId).IsValidNode())?.Assert($"The given hash does not represent a {nameof(NodeDedupIdentifier)}: {hash.AlgorithmId}");
            NodeAlgorithm = hash.AlgorithmId;
        }

        /// <nodoc />
        public NodeDedupIdentifier(byte[] hashResult, NodeAlgorithmId algorithmId)
            : base(hashResult, (byte)algorithmId)
        {
            Contract.Requires(hashResult != null);
            Contract.Check(algorithmId.IsValidNode())?.Assert($"The given hash does not represent a {nameof(NodeDedupIdentifier)}: {algorithmId}");
            NodeAlgorithm = (byte)algorithmId;
        }

        /// <nodoc />
        public static NodeDedupIdentifier CalculateIdentifierFromSerializedNode(byte[] bytes, HashType hashType)
        {
            Contract.Requires(bytes != null);
            Contract.Check(((NodeAlgorithmId)hashType.GetNodeAlgorithmId()).IsValidNode())?.Assert($"Cannot serialize from hash because hash type is invalid: {hashType}");
            return new NodeDedupIdentifier(Hasher.GetContentHash(bytes).ToHashByteArray(), hashType.GetNodeAlgorithmId());
        }

        /// <nodoc />
        public static NodeDedupIdentifier CalculateIdentifierFromSerializedNode(byte[] bytes, int offset, int count, HashType hashType)
        {
            Contract.Requires(bytes != null);
            Contract.Check(((NodeAlgorithmId)hashType.GetNodeAlgorithmId()).IsValidNode())?.Assert($"Cannot serialize from hash because hash type is invalid: {hashType}");
            return new NodeDedupIdentifier(Hasher.GetContentHash(bytes, offset, count).ToHashByteArray(), hashType.GetNodeAlgorithmId());
        }

        /// <nodoc />
        public static NodeDedupIdentifier CalculateIdentifierFromSerializedNode(ArraySegment<byte> bytes, HashType hashType)
        {
            Contract.Requires(bytes.Array != null);
            Contract.Check(((NodeAlgorithmId)hashType.GetNodeAlgorithmId()).IsValidNode())?.Assert($"Cannot serialize from hash because hash type is invalid: {hashType}");
            return new NodeDedupIdentifier(Hasher.GetContentHash(bytes.Array, bytes.Offset, bytes.Count).ToHashByteArray(), hashType.GetNodeAlgorithmId());
        }

        /// <nodoc />
        public static NodeDedupIdentifier Parse(string value)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(value));
            return (NodeDedupIdentifier)Create(value);
        }
    }
}
