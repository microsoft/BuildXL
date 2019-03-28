// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.VstsInterfaces
{
    /// <summary>
    /// Extension methods for ContentHashlists
    /// </summary>
    public static class ContentHashListExtensions
    {
        /// <summary>
        /// Gets a single hash representing the ContentHashList
        /// </summary>
        public static byte[] GetHashOfHashes(this ContentHashList contentHashList)
        {
            var rollingBlobIdentifier = new VsoHash.RollingBlobIdentifier();
            BlobIdentifier blobIdOfContentHashes = VsoHash.OfNothing.BlobId;
            for (int i = 0; i < contentHashList.Hashes.Count; i++)
            {
                BlobIdentifier blobId = BlobIdentifier.Deserialize(contentHashList.Hashes[i].ToHex());

                if (i != contentHashList.Hashes.Count - 1)
                {
                    rollingBlobIdentifier.Update(VsoHash.HashBlock(blobId.Bytes, blobId.Bytes.Length));
                }
                else
                {
                    blobIdOfContentHashes = rollingBlobIdentifier.Finalize(VsoHash.HashBlock(blobId.Bytes, blobId.Bytes.Length));
                }
            }

            return blobIdOfContentHashes.Bytes;
        }
    }
}
