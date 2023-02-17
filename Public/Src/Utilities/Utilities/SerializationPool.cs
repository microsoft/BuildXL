// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Buffers;
using System.IO;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Utilities
{
    /// <summary>
    /// A pooled handle for <see cref="ArrayBufferWriter{T}"/> used during serialization.
    /// </summary>
    public readonly struct PooledArrayBuffer : IDisposable
    {
        private readonly PooledObjectWrapper<ArrayBufferWriter<byte>> m_writer;
        private readonly int m_writtenCount;

        /// <nodoc />
        internal PooledArrayBuffer(PooledObjectWrapper<ArrayBufferWriter<byte>> writer, int writtenCount)
        {
            m_writer = writer;
            m_writtenCount = writtenCount;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            m_writer.Dispose();
        }

        /// <summary>
        /// Gets the span of bytes written by the span writer.
        /// </summary>
        public ReadOnlySpan<byte> WrittenSpan => m_writer.Instance.GetSpan().Slice(0, length: m_writtenCount);
    }

    /// <nodoc />
    public readonly struct PooledBuffer : IDisposable
    {
        /// <nodoc />
        public ReadOnlyMemory<byte> Buffer { get; }

        private readonly PooledObjectWrapper<StreamBinaryWriter> m_instance;

        /// <nodoc />
        public PooledBuffer(PooledObjectWrapper<StreamBinaryWriter> instance, int offset, int length)
        {
            m_instance = instance;
            if (!m_instance.Instance.Buffer.TryGetBuffer(out var segment))
            {
                throw new InvalidOperationException($"Could not obtain buffer from pooled {nameof(StreamBinaryWriter)} instance");
            }

            Buffer = segment.AsMemory(offset, length);
        }

        /// <nodoc />
        public static implicit operator ReadOnlyMemory<byte>(PooledBuffer buffer)
        {
            return buffer.Buffer;
        }

        /// <nodoc />
        public static implicit operator ReadOnlySpan<byte>(PooledBuffer buffer)
        {
            return buffer.Buffer.Span;
        }

        /// <nodoc />
        public byte[] ToArray()
        {
            return Buffer.ToArray();
        }

        /// <nodoc />
        public void Dispose()
        {
            m_instance.Dispose();
        }
    }

    /// <nodoc />
    public class SerializationPool
    {
        private readonly ObjectPool<StreamBinaryWriter> m_writerPool = new(static () => new StreamBinaryWriter(), static w => w.ResetPosition());
        private readonly ObjectPool<StreamBinaryReader> m_readerPool = new(static () => new StreamBinaryReader(), static r => { });

        private readonly ObjectPool<ArrayBufferWriter<byte>> m_arrayBufferWriters = new(() => new ArrayBufferWriter<byte>(), bw => (bw).Clear());

        /// <nodoc />
        public byte[] Serialize<TResult>(TResult instance, Action<TResult, BuildXLWriter> serializeFunc)
        {
            using var pooledBuffer = SerializePooled(instance, serializeFunc);
            return pooledBuffer.Buffer.ToArray();
        }

        /// <nodoc />
        public byte[] Serialize<TResult, TState>(TResult instance, TState state, Action<TState, TResult, BuildXLWriter> serializeFunc)
        {
            using var pooledBuffer = SerializePooled(instance, state, serializeFunc);
            return pooledBuffer.Buffer.ToArray();
        }

        /// <summary>
        /// A delegate used by <see cref="SerializePooled{T}(T,SerializeDelegate{T})"/>.
        /// </summary>
        public delegate void SerializeDelegate<in T>(T instance, ref SpanWriter writer);

        /// <summary>
        /// Serialize a given <paramref name="instance"/> with pooled <see cref="ArrayBufferWriter{T}"/>.
        /// </summary>
        public PooledArrayBuffer SerializePooled<T>(T instance, SerializeDelegate<T> serializeFunc)
        {
            var arrayBuffer = m_arrayBufferWriters.GetInstance();
            var writer = new SpanWriter(arrayBuffer.Instance);
            serializeFunc(instance, ref writer);

            return new PooledArrayBuffer(arrayBuffer, writer.Position);

        }

        /// <nodoc />
        public PooledBuffer SerializePooled<TResult>(TResult instance, Action<TResult, BuildXLWriter> serializeFunc)
        {
            return SerializePooled(instance, serializeFunc, static (f, r, w) => f(r, w));
        }

        /// <nodoc />
        public PooledBuffer SerializePooled<TResult, TState>(TResult instance, TState state, Action<TState, TResult, BuildXLWriter> serializeFunc)
        {
            var pooledWriter = m_writerPool.GetInstance();
            var streamWriter = pooledWriter.Instance;
            var bxlWriter = streamWriter.Writer;

            var bufferOffset = (int)streamWriter.Buffer.Position;
            serializeFunc(state, instance, bxlWriter);
            var length = ((int)streamWriter.Buffer.Position) - bufferOffset;

            return new PooledBuffer(pooledWriter, bufferOffset, length);
        }

        /// <nodoc />
        public TResult Deserialize<TResult>(ReadOnlyMemory<byte> bytes, Func<BuildXLReader, TResult> deserializeFunc)
        {
            using var pooledReader = m_readerPool.GetInstance();
            var reader = pooledReader.Instance;
            return reader.Deserialize(bytes, deserializeFunc);
        }
        
        /// <nodoc />
        public TResult Deserialize<TResult>(ReadOnlySpan<byte> bytes, Func<BuildXLReader, TResult> deserializeFunc)
        {
            using var pooledReader = m_readerPool.GetInstance();
            var reader = pooledReader.Instance;
            return reader.Deserialize(bytes, deserializeFunc);
        }

        /// <nodoc />
        public TResult Deserialize<TResult, TState>(ReadOnlySpan<byte> bytes, TState state, Func<TState, BuildXLReader, TResult> deserializeFunc)
        {
            using var pooledReader = m_readerPool.GetInstance();
            var reader = pooledReader.Instance;
            return reader.Deserialize(bytes, state, deserializeFunc);
        }

        /// <nodoc />
        public TResult Deserialize<TResult, TState>(ReadOnlyMemory<byte> bytes, TState state, Func<TState, BuildXLReader, TResult> deserializeFunc)
        {
            using var pooledReader = m_readerPool.GetInstance();
            var reader = pooledReader.Instance;
            return reader.Deserialize(bytes, state, deserializeFunc);
        }

        /// <nodoc />
        public TResult Deserialize<TResult>(Stream bytes, Func<BuildXLReader, TResult> deserializeFunc)
        {
            using var pooledReader = m_readerPool.GetInstance();
            var reader = pooledReader.Instance;
            return reader.Deserialize(bytes, deserializeFunc);
        }

        /// <nodoc />
        public TResult Deserialize<TResult, TState>(Stream bytes, TState state, Func<TState, BuildXLReader, TResult> deserializeFunc)
        {
            using var pooledReader = m_readerPool.GetInstance();
            var reader = pooledReader.Instance;
            return reader.Deserialize(bytes, state, deserializeFunc);
        }
    }
}
