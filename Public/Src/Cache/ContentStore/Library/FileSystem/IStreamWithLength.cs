// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;

namespace BuildXL.Cache.ContentStore.FileSystem
{
    /// <summary>
    /// A marker interface that specifies that the client of a <see cref="System.IO.Stream"/> instance to access <see cref="System.IO.Stream.Length"/> property.
    /// </summary>
    public interface IStreamWithLength
    {
        
    }

    /// <nodoc />
    public static class StreamWithLengthExtensions
    {
        /// <summary>
        /// Tries getting the length of a stream.
        /// </summary>
        public static long? TryGetStreamLength(this Stream stream)
        {
            if (stream is IStreamWithLength or MemoryStream)
            {
                return stream.Length;
            }

            if (stream.CanSeek)
            {
                return stream.Length;
            }

            return null;
        }
    }
}
