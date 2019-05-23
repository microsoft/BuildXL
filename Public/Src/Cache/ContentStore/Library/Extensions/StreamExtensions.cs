// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Extensions
{
    /// <summary>
    ///     Useful extensions to Stream classes.
    /// </summary>
    public static class StreamExtensions
    {
        /// <summary>
        ///     Read all bytes from a stream.
        /// </summary>
        /// <param name="stream">Source strea</param>
        /// <param name="dispose">If true, dispose source stream when done</param>
        public static async Task<byte[]> GetBytes(this Stream stream, bool dispose = true)
        {
            using (var content = new MemoryStream())
            {
                await stream.CopyToWithFullBufferAsync(content, FileSystemConstants.FileIOBufferSize);

                if (dispose)
                {
#pragma warning disable AsyncFixer02
                    stream.Dispose();
#pragma warning restore AsyncFixer02
                }

                return content.ToArray();
            }
        }

        /// <summary>
        ///     Copy all or some bytes from one stream to another.
        /// </summary>
        public static void CopyTo(this Stream sourceStream, Stream destinationStream, int bufferSize, long size)
        {
            if (size < 0)
            {
                sourceStream.CopyTo(destinationStream, bufferSize);
                return;
            }

            var buffer = new byte[bufferSize];
            var bytesLeft = size;

            while (bytesLeft > 0)
            {
                var bytesToRead = (int)Math.Min(bytesLeft, bufferSize);
                var bytesRead = sourceStream.Read(buffer, 0, bytesToRead);

                if (bytesRead == 0)
                {
                    throw new CacheException($"Read {bytesRead} bytes from ClientStream, but expected to read {bytesToRead} bytes");
                }

                destinationStream.Write(buffer, 0, bytesRead);

                bytesLeft -= bytesRead;
            }
        }
    }
}
