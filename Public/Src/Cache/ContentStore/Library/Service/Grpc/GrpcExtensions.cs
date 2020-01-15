// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Hashing;
using ContentStore.Grpc;
using Google.Protobuf;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Extension methods for GRPC code.
    /// </summary>
    public static class GrpcExtensions
    {
        /// <summary>
        /// Converts a bytestring to a contenthash
        /// </summary>
        public static ContentHash ToContentHash(this ByteString byteString, HashType hashType)
        {
            byte[] contentHashByteArray = byteString.ToByteArray();
            return new ContentHash(hashType, contentHashByteArray);
        }

        /// <summary>
        /// Converts a <see cref="CopyFileRequest"/> to a <see cref="ContentHash"/>.
        /// </summary>
        public static ContentHash GetContentHash(this CopyFileRequest request) => request.ContentHash.ToContentHash((HashType)request.HashType);

        /// <summary>
        /// Converts a bytestring to a contenthash
        /// </summary>
        public static ByteString ToByteString(in this ContentHash byteString)
        {
            byte[] hashByteArray = byteString.ToHashByteArray();
            return ByteString.CopyFrom(hashByteArray, 0, hashByteArray.Length);
        }
    }
}
