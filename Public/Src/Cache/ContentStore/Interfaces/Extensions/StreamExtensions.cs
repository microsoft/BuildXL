// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;

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

            var settings = new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };
            var serializer = new DataContractJsonSerializer(typeof(T), settings);
            stream.Position = 0;
            // TODO: suppressing this for now, but the clients need to respect that the result of this method may be null.
            return (T)serializer.ReadObject(stream)!;
        }

        /// <summary>
        ///     Read bytes from a stream, issuing multiple reads if necessary.
        /// </summary>
        public static async Task<int> ReadBytesAsync(
            this Stream stream,
            byte[] buffer,
            int count,
            CancellationToken cancellationToken = default)
        {
            Contract.Requires(stream != null);
            Contract.Requires(buffer != null);
            Contract.Requires(count >= 0);
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

                bytesRead = await stream.ReadAsync(buffer, offset, bytesToRead, cancellationToken).ConfigureAwait(false);
                offset += bytesRead;
            }
            while (bytesRead > 0);

            return offset;
        }

        /// <summary>
        ///     Copies from one stream to another with writes guaranteed to be called with a full buffer (except the last call)
        /// </summary>
        public static async Task CopyToWithFullBufferAsync(
            this Stream stream,
            Stream destination,
            byte[]? buffer = null,
            CancellationToken cancellationToken = default)
        {
            Contract.Requires(stream != null);
            Contract.Requires(destination != null);
            Contract.Requires(stream.CanRead);
            Contract.Requires(destination.CanWrite);

            Pool<byte[]>.PoolHandle? handle = null;
            if (buffer == null)
            {
                handle = GlobalObjectPools.FileIOBuffersArrayPool.Get();
                buffer = handle.Value.Value;
            }
            Contract.Assert(buffer is not null);

            try
            {
                int bytesRead;
                while ((bytesRead = await stream.ReadBytesAsync(buffer, buffer.Length, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                }

                // Ensure that the data we've just written is flushed. This is actually assumed by all callers of the code,
                // and it's important because the data is not guaranteed to be written to the underlying storage until this
                // is called.
                // There's a number of issues doing this prevents:
                //  1. If the system fails after the write but before the flush, the data may be lost. This has happened
                //     before in production and can cause the cache to incorrectly succeed at inserting a file which
                //     doesn't actually have the data yet.
                //  2. When the data isn't flushed, the flush will actually happen inside the Dispose statement. Since
                //     dispose is actually sync, that triggers a sync-over-async issue that can cause deadlocks.
                try
                {
                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (NotImplementedException)
                {
                    // Ignore the exception if the stream doesn't support flushing. This is a common case for streams that
                    // are used for testing.
                }
            }
            finally
            {
                handle?.Dispose();
            }
        }
    }
}
