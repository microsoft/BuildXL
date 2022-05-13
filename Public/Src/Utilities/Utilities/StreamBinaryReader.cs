// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Helper class that can be used for binary deserialization of multiple objects in memory efficient way
    /// by reusing instances.
    /// </summary>
    /// <remarks>
    /// The instance of this type is not thread-safe.
    /// </remarks>
    public sealed class StreamBinaryReader
    {
        private readonly SwappableStream m_stream;
        private readonly BuildXLReader m_reader;

        private MemoryStream m_memoryStream;
        private ReadOnlyMemoryStream m_readOnlyMemoryStream;

        /// <nodoc />
        public StreamBinaryReader()
        {
            m_stream = new SwappableStream(Stream.Null);
            m_reader = BuildXLReader.Create(m_stream, leaveOpen: true);
        }

        /// <nodoc />
        public T Deserialize<T>(ReadOnlySpan<byte> data, Func<BuildXLReader, T> deserializeFunc)
        {
            SetupWithMemoryStream(data);
            return deserializeFunc(m_reader);
        }

        /// <nodoc />
        public T Deserialize<T>(ReadOnlyMemory<byte> data, Func<BuildXLReader, T> deserializeFunc)
        {
            SetupWithReadOnlyMemoryStream(data);
            return deserializeFunc(m_reader);
        }

        /// <nodoc />
        public T Deserialize<T>(Stream stream, Func<BuildXLReader, T> deserializeFunc)
        {
            m_stream.Swap(stream);
            return deserializeFunc(m_reader);
        }

        /// <nodoc />
        public TResult Deserialize<TResult, TState>(ReadOnlySpan<byte> data, TState state, Func<TState, BuildXLReader, TResult> deserializeFunc)
        {
            SetupWithMemoryStream(data);
            return deserializeFunc(state, m_reader);
        }

        /// <nodoc />
        public TResult Deserialize<TResult, TState>(ReadOnlyMemory<byte> data, TState state, Func<TState, BuildXLReader, TResult> deserializeFunc)
        {
            SetupWithReadOnlyMemoryStream(data);
            return deserializeFunc(state, m_reader);
        }

        /// <nodoc />
        public TResult Deserialize<TResult, TState>(Stream stream, TState state, Func<TState, BuildXLReader, TResult> deserializeFunc)
        {
            m_stream.Swap(stream);
            return deserializeFunc(state, m_reader);
        }

        /// <nodoc />
        public IReadOnlyList<T> DeserializeSequence<T>(ReadOnlySpan<byte> data, Func<BuildXLReader, T> deserializeFunc)
        {
            SetupWithMemoryStream(data);

            var deserialized = new List<T>();
            while (m_stream.Position < data.Length)
            {
                deserialized.Add(deserializeFunc(m_reader));
            }
            return deserialized;
        }

        /// <nodoc />
        public IReadOnlyList<T> DeserializeSequence<T>(ReadOnlyMemory<byte> data, Func<BuildXLReader, T> deserializeFunc)
        {
            SetupWithReadOnlyMemoryStream(data);

            var deserialized = new List<T>();
            while (m_stream.Position < data.Length)
            {
                deserialized.Add(deserializeFunc(m_reader));
            }
            return deserialized;
        }

        private void SetupWithMemoryStream(ReadOnlySpan<byte> data)
        {
            if (m_memoryStream is null)
            {
                m_memoryStream = new MemoryStream();
            }
            m_stream.Swap(m_memoryStream);

            m_memoryStream.Position = 0;

#if NET_COREAPP
            m_memoryStream.Write(data);
#else
            if (m_memoryStream.TryGetBuffer(out var segment) && segment.Count > data.Length)
            {
                data.CopyTo(segment.AsSpan());
            }
            else
            {
                // Warning: extra allocation here. This should never happen.
                m_memoryStream.Write(buffer: data.ToArray(), offset: 0, count: data.Length);
            }
#endif

            m_memoryStream.Position = 0;
        }

        private void SetupWithReadOnlyMemoryStream(ReadOnlyMemory<byte> data)
        {
            if (m_readOnlyMemoryStream is null)
            {
                m_readOnlyMemoryStream = new ReadOnlyMemoryStream();
            }
            m_readOnlyMemoryStream.Swap(data);
            m_stream.Swap(m_readOnlyMemoryStream);
        }
    }
}
