// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Security.Cryptography;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Extension of the SHA512Managed class to implement SHA512 truncated (first 256 bytes)
    /// </summary>
    /// <remarks>The NTFS volume-level deduplication store uses this as their hashing algorithm.</remarks>
    public sealed class DedupChunkHashAlgorithm : HashAlgorithm
    {
        private static readonly byte[] EmptyArray = new byte[0];

        private readonly SHA512 _hasher = new SHA512CryptoServiceProvider();

        /// <inheritdoc />
        public override byte[] Hash => TruncateTo256Bits(base.Hash);

        /// <inheritdoc />
        public override int HashSize => 8 * DedupSingleChunkHashInfo.Length;

        /// <inheritdoc />
        public override void Initialize()
        {
            _hasher.Initialize();
        }

        /// <inheritdoc />
        protected override void HashCore(byte[] inputBuffer, int inputOffset, int inputCount)
        {
            _hasher.TransformBlock(inputBuffer, inputOffset, inputCount, null, 0);
        }

        internal void HashCoreInternal(byte[] inputBuffer, int inputOffset, int inputCount)
        {
            HashCore(inputBuffer, inputOffset, inputCount);
        }

        /// <inheritdoc />
        protected override byte[] HashFinal()
        {
            _hasher.TransformFinalBlock(EmptyArray, 0, 0);
            return TruncateTo256Bits(_hasher.Hash);
        }

        internal byte[] HashFinalInternal()
        {
            return HashFinal();
        }

        private static byte[] TruncateTo256Bits(byte[] bytes)
        {
            return bytes.Take(DedupSingleChunkHashInfo.Length).ToArray();
        }
    }
}
