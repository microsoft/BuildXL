// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Helper class that can be used for binary deserialization of multiple objects in memory efficient way
    /// by reusing <see cref="MemoryStream"/> and <see cref="BinaryReader"/> instances.
    /// </summary>
    /// <remarks>
    /// The instance of this type is not thread-safe.
    /// </remarks>
    public sealed class StreamBinaryReader 
    {
        private readonly MemoryStream _eventReadBuffer;
        private readonly BuildXLReader _eventBufferReader;

        /// <nodoc />
        public StreamBinaryReader()
        {
            _eventReadBuffer = new MemoryStream();
            _eventBufferReader = BuildXLReader.Create(_eventReadBuffer);
        }

        /// <nodoc />
        public T Deserialize<T>(ArraySegment<byte> data, Func<BuildXLReader, T> deserializeFunc)
        {
            _eventReadBuffer.Position = 0;
            _eventReadBuffer.Write(data.Array, data.Offset, data.Count);
            _eventReadBuffer.Position = 0;

            return deserializeFunc(_eventBufferReader);
        }

        /// <nodoc />
        public TResult Deserialize<TResult, TState>(ArraySegment<byte> data, TState state, Func<TState, BuildXLReader, TResult> deserializeFunc)
        {
            _eventReadBuffer.Position = 0;
            _eventReadBuffer.Write(data.Array, data.Offset, data.Count);
            _eventReadBuffer.Position = 0;

            return deserializeFunc(state, _eventBufferReader);
        }

        /// <nodoc />
        public IEnumerable<T> DeserializeSequence<T>(ArraySegment<byte> data, Func<BuildXLReader, T> deserializeFunc)
        {
            _eventReadBuffer.Position = 0;
            _eventReadBuffer.Write(data.Array, data.Offset, data.Count);
            _eventReadBuffer.Position = 0;

            while (_eventReadBuffer.Position < data.Count)
            {
                yield return deserializeFunc(_eventBufferReader);
            }
        }
    }
}
