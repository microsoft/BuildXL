// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Newtonsoft.Json;

namespace BuildXL.Cache.MemoizationStore.VstsInterfaces.Blob
{
    /// <summary>
    /// Extension methods for extracting and adding blob contenthashlists
    /// </summary>
    /// <remarks>
    /// This code is a contract between the client and service on how to pack/unpack metadata records
    /// DO NOT CHANGE unless you maintain compatibility.
    /// </remarks>
    public static class BlobContentHashListExtensions
    {
        /// <summary>
        /// Gets a ContentHashlistWithDeterminism stored in the blob.
        /// </summary>
        public static async Task<Result<ContentHashListWithDeterminism>> UnpackFromBlob(
            Func<ContentHash, CancellationToken, Task<Result<Stream>>> streamFactory,
            BlobIdentifier blobId)
        {
            Result<Stream> sourceStreamResult = await streamFactory(
                new ContentHash(HashType.Vso0, blobId.Bytes),
                CancellationToken.None);

            if (!sourceStreamResult.Succeeded)
            {
                return new Result<ContentHashListWithDeterminism>(sourceStreamResult);
            }

            try
            {
                using (Stream fileStream = sourceStreamResult.Value)
                {
                    using (GZipStream stream = new GZipStream(fileStream, CompressionMode.Decompress))
                    {
                        using (var streamReader = new StreamReader(stream))
                        {
                            using (JsonTextReader jsonreader = new JsonTextReader(streamReader))
                            {
                                JsonSerializer serializer = new JsonSerializer();
                                serializer.Converters.Add(new ContentHashListWithDeterminismConverter());
                                return
                                    new Result<ContentHashListWithDeterminism>(
                                        serializer.Deserialize<ContentHashListWithDeterminism>(jsonreader));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return new Result<ContentHashListWithDeterminism>(ex);
            }
        }

        /// <summary>
        ///  Packs a contenthashlist into a blob and returns a blob id.
        /// </summary>
        public static async Task<Result<ContentHash>> PackInBlob(
            Func<Stream, CancellationToken, Task<Result<ContentHash>>> putStreamFunc,
            ContentHashListWithDeterminism contentHashListWithDeterminism)
        {
            try
            {
                using (Stream memoryStream = new MemoryStream())
                {
                    using (
                        var compressedStream = new GZipStream(memoryStream, CompressionMode.Compress, leaveOpen: true))
                    {
                        using (StreamWriter sw = new StreamWriter(
                            compressedStream,
                            Encoding.UTF8,
                            1024,
                            leaveOpen: true))
                        {
                            var jsonSerializer = new JsonSerializer();
                            jsonSerializer.Converters.Add(new ContentHashListWithDeterminismConverter());
                            jsonSerializer.Serialize(sw, contentHashListWithDeterminism);
                            await sw.FlushAsync();
                        }
                    }

                    memoryStream.Position = 0;

                    var contentHashResult = await putStreamFunc(
                        memoryStream,
                        CancellationToken.None);

                    if (!contentHashResult.Succeeded)
                    {
                        return new Result<ContentHash>(contentHashResult);
                    }

                    return new Result<ContentHash>(contentHashResult.Value);
                }
            }
            catch (Exception ex)
            {
                return new Result<ContentHash>(ex);
            }
        }
    }
}
