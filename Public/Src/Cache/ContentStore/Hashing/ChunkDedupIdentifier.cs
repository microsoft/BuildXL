using System;
using System.Diagnostics.ContractsLight;

#pragma warning disable SA1600 // Elements must be documented
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace BuildXL.Cache.ContentStore.Hashing
{
    public sealed class ChunkDedupIdentifier : DedupIdentifier
    {
        public const byte ChunkAlgorithmId = 01;

        private static readonly IContentHasher chunkHasher = DedupSingleChunkHashInfo.Instance.CreateContentHasher();

        public ChunkDedupIdentifier(HashAndAlgorithm hash)
            : base(hash)
        {
            Contract.Requires(hash.Bytes != null);
            Validate();
        }

        public ChunkDedupIdentifier(byte[] hashResult)
            : base(hashResult, ChunkAlgorithmId)
        {
            Contract.Requires(hashResult != null);
            Validate();
        }

        public static ChunkDedupIdentifier CalculateIdentifier(byte[] bytes)
        {
            Contract.Requires(bytes != null);
            return new ChunkDedupIdentifier(chunkHasher.GetContentHash(bytes).ToHashByteArray());
        }

        public static ChunkDedupIdentifier CalculateIdentifier(byte[] bytes, int offset, int count)
        {
            Contract.Requires(bytes != null);
            return new ChunkDedupIdentifier(chunkHasher.GetContentHash(bytes, offset, count).ToHashByteArray());
        }

        public static ChunkDedupIdentifier CalculateIdentifier(ArraySegment<byte> bytes)
        {
            Contract.Requires(bytes.Array != null);
            return new ChunkDedupIdentifier(chunkHasher.GetContentHash(bytes.Array, bytes.Offset, bytes.Count).ToHashByteArray());
        }

        public static ChunkDedupIdentifier Parse(string value)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(value));
            return (ChunkDedupIdentifier)Create(value);
        }

        private void Validate()
        {
            Contract.Check(AlgorithmId == ChunkAlgorithmId)?.Assert($"The given algorithm does not represent a {nameof(ChunkDedupIdentifier)}. Actual: {AlgorithmId} Expected: {ChunkAlgorithmId}");
        }
    }
}