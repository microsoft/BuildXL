// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// BlobIdentifier helpers
    /// </summary>
    public static class BlobIdentifierHelperExtensions
    {
        /// <summary>
        /// Creates a <see cref="BlobIdentifier"/> from the stream provided
        /// </summary>
        /// <param name="blob">The content stream against which the identifier should be created</param>
        /// <returns>
        /// A <see cref="BlobIdentifier"/> instance representing the unique identifier for binary the content.
        /// </returns>
        public static BlobIdentifier CalculateBlobIdentifier(this Stream blob)
        {
            blob.Position = 0;
            var result = VsoHash.CalculateBlobIdentifier(blob);
            blob.Position = 0;
            return result;
        }

        /// <summary>
        /// Creates a <see cref="BlobIdentifierWithBlocks"/> from the stream provided
        /// </summary>
        /// <param name="blob">The content stream against which the identifier should be created</param>
        /// <returns>
        /// A <see cref="BlobIdentifierWithBlocks"/> instance representing the unique identifier for binary the content.
        /// </returns>
        public static BlobIdentifierWithBlocks CalculateBlobIdentifierWithBlocks(this Stream blob)
        {
            blob.Position = 0;
            var result = VsoHash.CalculateBlobIdentifierWithBlocks(blob);
            blob.Position = 0;
            return result;
        }

        /// <summary>
        /// Creates a <see cref="BlobIdentifierWithBlocks"/> from the stream provided
        /// </summary>
        /// <param name="blob">The content stream against which the identifier should be created</param>
        /// <returns>
        /// A <see cref="BlobIdentifierWithBlocks"/> instance representing the unique identifier for binary the content.
        /// </returns>
        public static async Task<BlobIdentifierWithBlocks> CalculateBlobIdentifierWithBlocksAsync(this Stream blob)
        {
            blob.Position = 0;
            var result = await VsoHash.CalculateBlobIdentifierWithBlocksAsync(blob).ConfigureAwait(false);
            blob.Position = 0;
            return result;
        }

        /// <summary>
        /// Creates a <see cref="BlobIdentifierWithBlocks"/> from the bytes provided
        /// </summary>
        /// <param name="blob">The content against which the identifier should be created</param>
        /// <returns>
        /// A <see cref="BlobIdentifierWithBlocks"/> instance representing the unique identifier for binary the content.
        /// </returns>
        public static BlobIdentifierWithBlocks CalculateBlobIdentifierWithBlocks(this byte[] blob)
        {
            using (var stream = new MemoryStream(blob))
            {
                return VsoHash.CalculateBlobIdentifierWithBlocks(stream);
            }
        }

        /// <summary>
        /// Creates a <see cref="BlobIdentifier"/> from the stream provided
        /// </summary>
        /// <param name="blob">The content bytes against which the identifier should be created.</param>
        /// <returns>
        /// A <see cref="BlobIdentifier"/> instance representing the unique identifier for binary the content.
        /// </returns>
        public static BlobIdentifier CalculateBlobIdentifier(this byte[] blob)
        {
            return VsoHash.CalculateBlobIdentifier(blob);
        }
    }
}
