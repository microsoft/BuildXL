// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Interfaces.Extensions
{
    /// <summary>
    ///     Useful extensions to Stream classes.
    /// </summary>
    public static class StreamExtensions
    {
        /// <summary>
        ///     Serialize an object instance to JSON.
        /// </summary>
        public static void SerializeToJSON<T>(this T obj, Stream stream)
        {
            Contract.Requires(obj != null);
            Contract.Requires(stream != null);

            var settings = new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };
            var serializer = new DataContractJsonSerializer(typeof(T), settings);
            serializer.WriteObject(stream, obj);
        }

        /// <summary>
        ///     Create an object instance from JSON.
        /// </summary>
        public static T DeserializeFromJSON<T>(this Stream stream)
        {
            Contract.Requires(stream != null);

            var settings = new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat = true};
            var serializer = new DataContractJsonSerializer(typeof(T), settings);
            stream.Position = 0;
            return (T)serializer.ReadObject(stream);
        }

        /// <summary>
        ///     Read bytes from a stream, issuing multiple reads if necessary.
        /// </summary>
        /// <param name="stream">Stream to read from.</param>
        /// <param name="buffer">Buffer to write bytes to.</param>
        /// <param name="count">Number of bytes to read.</param>
        public static async Task<int> ReadBytes(this Stream stream, byte[] buffer, int count)
        {
            Contract.Requires(stream != null);
            Contract.Requires(buffer != null);
            Contract.Requires(count <= buffer.Length);

            int bytesRead;
            int offset = 0;
            do
            {
                int bytesToRead = count - offset;
                if (bytesToRead == 0)
                {
                    break;
                }

                bytesRead = await stream.ReadAsync(buffer, offset, bytesToRead).ConfigureAwait(false);
                offset += bytesRead;
            }
            while (bytesRead > 0);

            return offset;
        }

        /// <summary>
        ///     Copies from one stream to another with writes guaranteed to be called with a full buffer (except the last call)
        /// </summary>
        /// <param name="stream">Source stream</param>
        /// <param name="destination">Destination stream</param>
        /// <param name="bufferSize">Size of the buffer to fill for writes</param>
        public static async Task CopyToWithFullBufferAsync(this Stream stream, Stream destination, int bufferSize)
        {
            Contract.Requires(stream != null);           
            Contract.Requires(destination != null);
            Contract.Requires(stream.CanRead);
            Contract.Requires(destination.CanWrite);
            Contract.Requires(bufferSize > 0);

            var buffer = new byte[bufferSize];

            int bytesRead;
            while ((bytesRead = await stream.ReadBytes(buffer, buffer.Length).ConfigureAwait(false)) != 0)
            {
                await destination.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
            }
        }
    }
}
