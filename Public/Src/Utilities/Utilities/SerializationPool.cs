// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;

namespace BuildXL.Utilities
{
    /// <nodoc />
    public struct PooledBuffer : IDisposable
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
        private readonly ObjectPool<StreamBinaryWriter> m_writerPool = new ObjectPool<StreamBinaryWriter>(static () => new StreamBinaryWriter(), static w => w.ResetPosition());
        private readonly ObjectPool<StreamBinaryReader> m_readerPool = new ObjectPool<StreamBinaryReader>(static () => new StreamBinaryReader(), static r => { });

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
