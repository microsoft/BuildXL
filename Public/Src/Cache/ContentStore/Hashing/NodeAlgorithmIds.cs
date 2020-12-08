using System.Collections.Generic;
using System.ComponentModel;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Dedup node algorithm id's corresponding to various chunk sizes.
    /// They denote the last byte in the hash/dedup identifier.
    /// Hash 32 bytes + 1 byte (algorithm id).
    /// </summary>
    public enum NodeAlgorithmId : byte
    {
        /// <summary>
        ///     Dedup node - collection of 64K sized chunks.
        /// </summary>
        Node64K = 02,

        /// <summary>
        ///     Dedup node - collection of 1MB sized chunks.
        /// </summary>
        Node1024K = 06,

        //---*** Un-used ***---//

        /// <summary>
        ///     Dedup node - collection of 128K sized chunks.
        /// </summary>
        Node128K = 03,

        /// <summary>
        ///     Dedup node - collection of 256K sized chunks.
        /// </summary>
        Node256K = 04,

        /// <summary>
        ///     Dedup node - collection of 512K sized chunks.
        /// </summary>
        Node512K = 05,

        /// <summary>
        ///     Dedup node - collection of 2MB sized chunks.
        /// </summary>
        Node2056K = 07,

        /// <summary>
        ///     Dedup node - collection of 4MB sized chunks.
        /// </summary>
        Node4192K = 08,
    }

    /// <summary>
    ///     DedupUtility - helper class.
    /// </summary>
    public static class AlgorithmIdExtensions
    {
        /// <summary>
        ///     IsNodeAlgorithmId - determines if the given algorithm id is a valid/supported node id.
        /// </summary>
        /// <param name="algorithmId">The given algorithm id.</param>
        /// <returns>True if valid, false otherwise.</returns>
        public static bool IsValidNode(this NodeAlgorithmId algorithmId)
        {
            switch (algorithmId)
            {
                // TODO: Chunk size optimization
                case NodeAlgorithmId.Node1024K:
                case NodeAlgorithmId.Node64K:
                    return true;
                default:
                    throw new InvalidEnumArgumentException($"Unsupported algorithm id {algorithmId} of enum type: {nameof(NodeAlgorithmId)} encountered.");
            }
        }
    
        /// <nodoc />
        public static IContentHasher GetContentHasher(this NodeAlgorithmId algorithmId)
        {
            switch (algorithmId)
            {
                case NodeAlgorithmId.Node1024K:
                    return Dedup1024KHashInfo.Instance.CreateContentHasher();
                case NodeAlgorithmId.Node64K:
                    return DedupNode64KHashInfo.Instance.CreateContentHasher();
                default:
                    throw new InvalidEnumArgumentException($"No hasher found for unsupported {nameof(NodeAlgorithmId)} : {algorithmId}.");
            }
        }         
    }
}
