// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;

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
        /// <param name="stream">Source stream</param>
        /// <param name="dispose">If true, dispose source stream when done</param>
        public static async Task<byte[]> GetBytes(this Stream stream, bool dispose = true)
        {
            using (var content = new MemoryStream())
            {
                await stream.CopyToWithFullBufferAsync(content);

                if (dispose)
                {
                    stream.Dispose();
                }

                return content.ToArray();
            }
        }
    }
}
