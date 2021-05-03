// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities
{
    /// <nodoc />
    public class SerializationPool
    {
        private readonly ObjectPool<StreamBinaryWriter> m_writerPool = new ObjectPool<StreamBinaryWriter>(() => new StreamBinaryWriter(), w => w.ResetPosition());
        private readonly ObjectPool<StreamBinaryReader> m_readerPool = new ObjectPool<StreamBinaryReader>(() => new StreamBinaryReader(), r => { });

        /// <nodoc />
        public byte[] Serialize<TResult>(TResult instance, Action<TResult, BuildXLWriter> serializeFunc)
        {
            using var pooledWriter = m_writerPool.GetInstance();
            var writer = pooledWriter.Instance.Writer;
            serializeFunc(instance, writer);
            // TODO: This still causes an allocation. StreamBinaryWriter can create a MemoryStream with a given buffer
            // and then expose the buffer back via 'Memory<byte>' (or via a wrapper that will return poolWriter to the pool
            return pooledWriter.Instance.Buffer.ToArray();
        }

        /// <nodoc />
        public byte[] Serialize<TResult, TState>(TResult instance, TState state, Action<TState, TResult, BuildXLWriter> serializeFunc)
        {
            using var pooledWriter = m_writerPool.GetInstance();
            var writer = pooledWriter.Instance.Writer;
            serializeFunc(state, instance, writer);
            return pooledWriter.Instance.Buffer.ToArray();
        }

        /// <nodoc />
        public TResult Deserialize<TResult>(byte[] bytes, Func<BuildXLReader, TResult> deserializeFunc)
        {
            using PooledObjectWrapper<StreamBinaryReader> pooledReader = m_readerPool.GetInstance();
            var reader = pooledReader.Instance;
            return reader.Deserialize(
                new ArraySegment<byte>(bytes),
                deserializeFunc);
        }

        /// <nodoc />
        public TResult Deserialize<TResult, TState>(byte[] bytes, TState state, Func<TState, BuildXLReader, TResult> deserializeFunc)
        {
            using PooledObjectWrapper<StreamBinaryReader> pooledReader = m_readerPool.GetInstance();
            var reader = pooledReader.Instance;
            return reader.Deserialize(
                new ArraySegment<byte>(bytes),
                state,
                deserializeFunc);
        }
    }
}
