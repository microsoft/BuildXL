// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

#if NET_FRAMEWORK
        private readonly SHA512 _hasher = new SHA512Cng();
#else
        private readonly SHA512 _hasher = new SHA512Managed();
#endif

        /// <inheritdoc />
        public override byte[] Hash => TruncateTo256Bits(base.Hash);

        /// <inheritdoc />
        public override int HashSize => 8 * DedupChunkHashInfo.Length;

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

        /// <inheritdoc />
        protected override byte[] HashFinal()
        {
            _hasher.TransformFinalBlock(EmptyArray, 0, 0);
            return TruncateTo256Bits(_hasher.Hash);
        }

        private static byte[] TruncateTo256Bits(byte[] bytes)
        {
            return bytes.Take(DedupChunkHashInfo.Length).ToArray();
        }
    }
}
