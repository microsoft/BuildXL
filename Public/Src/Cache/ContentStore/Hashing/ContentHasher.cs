// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Shared implementation for all concrete classes.
    /// </summary>
    public class ContentHasher<T> : IContentHasher
        where T : HashAlgorithm, new()
    {
        /// <summary>
        ///     Object pool that holds all instantiated hash algorithms for each hash type.
        /// </summary>
        /// <remarks>
        ///     Cap the number of idle reserve instances in the pool so as to not unnecessarily hold large amounts of memory
        /// </remarks>
        private readonly Pool<HashAlgorithm> _algorithmsPool = new Pool<HashAlgorithm>(() => new T(), maxReserveInstances: 10);

        private readonly ByteArrayPool _bufferPool = new ByteArrayPool(FileSystemConstants.FileIOBufferSize);

        private long _calls;
        private long _ticks;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentHasher{T}" /> class for a specific hash type.
        /// </summary>
        public ContentHasher(HashInfo info)
        {
            Contract.Requires(info != null);
            Contract.Requires(info.HashType != HashType.Unknown);

            Info = info;
        }

        /// <inheritdoc />
        public HashInfo Info { get; }

        /// <summary>
        ///     Gets current number of algorithm already allocated and available.
        /// </summary>
        public int PoolSize => _algorithmsPool.Size;

        /// <inheritdoc />
        public void Dispose()
        {
            while (_algorithmsPool.Size > 0)
            {
                var pooledItem = _algorithmsPool.Get();
                // Need to dispose the Value not the wrapper from the pool.
                pooledItem.Value.Dispose();
            }
        }

        /// <inheritdoc />
        public HasherToken CreateToken() => new HasherToken(_algorithmsPool.Get());

        /// <inheritdoc />
        public async Task<ContentHash> GetContentHashAsync(Stream content)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using (var bufferHandle = _bufferPool.Get())
                {
                    var buffer = bufferHandle.Value;

                    using (var hasherHandle = CreateToken())
                    {
                        var hasher = hasherHandle.Hasher;
                        int bytesRead;
                        while ((bytesRead = await content.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
                        {
                            hasher.TransformBlock(buffer, 0, bytesRead, null, 0);
                        }

                        hasher.TransformFinalBlock(buffer, 0, 0);
                        var hashBytes = hasher.Hash;
                        return new ContentHash(Info.HashType, hashBytes);
                    }
                }
            }
            finally
            {
                stopwatch.Stop();
                Interlocked.Add(ref _ticks, stopwatch.Elapsed.Ticks);
                Interlocked.Increment(ref _calls);
            }
        }

        /// <inheritdoc />
        public ContentHash GetContentHash(byte[] content)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using (var hasherToken = CreateToken())
                {
                    var hashBytes = hasherToken.Hasher.ComputeHash(content);
                    return new ContentHash(Info.HashType, hashBytes);
                }
            }
            finally
            {
                stopwatch.Stop();
                Interlocked.Add(ref _ticks, stopwatch.Elapsed.Ticks);
                Interlocked.Increment(ref _calls);
            }
        }

        /// <inheritdoc />
        public ContentHash GetContentHash(byte[] content, int offset, int count)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using (var hasherToken = CreateToken())
                {
                    var hashBytes = hasherToken.Hasher.ComputeHash(content, offset, count);
                    return new ContentHash(Info.HashType, hashBytes);
                }
            }
            finally
            {
                stopwatch.Stop();
                Interlocked.Add(ref _ticks, stopwatch.Elapsed.Ticks);
                Interlocked.Increment(ref _calls);
            }
        }

        /// <inheritdoc />
        public CounterSet GetCounters()
        {
            var counters = new CounterSet();

            if (_calls > 0)
            {
                counters.Add("ContentHasherCalls", Interlocked.Read(ref _calls));
                counters.Add("ContentHasherMs", (long)new TimeSpan(Interlocked.Read(ref _ticks)).TotalMilliseconds);
                counters.Add("ContentHasherMaxConcurrency", _algorithmsPool.Size);
            }

            return counters;
        }

        /// <summary>
        ///     Create a wrapping stream that calculates the content hash.
        /// </summary>
        public HashingStream CreateReadHashingStream(Stream stream, long parallelHashingFileSizeBoundary = -1)
        {
            return new HashingStreamImpl(stream, this, CryptoStreamMode.Read, useParallelHashing: parallelHashingFileSizeBoundary >= 0, parallelHashingFileSizeBoundary);
        }

        /// <summary>
        ///     Create a wrapping stream that calculates the content hash.
        /// </summary>
        public HashingStream CreateWriteHashingStream(Stream stream, long parallelHashingFileSizeBoundary = -1)
        {
            return new HashingStreamImpl(stream, this, CryptoStreamMode.Write, useParallelHashing: parallelHashingFileSizeBoundary >= 0, parallelHashingFileSizeBoundary);
        }

        /// <summary>
        ///     A stream that wraps an existing stream and computes a hash of the bytes that pass through it.
        /// </summary>
        private sealed class HashingStreamImpl : HashingStream
        {
            private static readonly byte[] EmptyByteArray = new byte[0];
            private static readonly Task<bool> TrueTask = Task.FromResult(true);

            private static readonly Stopwatch Timer = Stopwatch.StartNew();

            private readonly ActionBlock<Pool<Buffer>.PoolHandle> _hashingBufferBlock;

            private readonly Stream _baseStream;
            private readonly ContentHasher<T> _hasher;

            private readonly CryptoStreamMode _streamMode;
            private readonly long _parallelHashingFileSizeBoundary;
            private readonly bool _useParallelHashing;
            private readonly HasherToken _hasherHandle;
            private readonly HashAlgorithm _hashAlgorithm;
            private bool _finalized;
            private bool _disposed;

            private long _bytesHashed = 0;

            private long _ticksSpentHashing;

            public HashingStreamImpl(Stream stream, ContentHasher<T> hasher, CryptoStreamMode mode, bool useParallelHashing, long parallelHashingFileSizeBoundary)
            {
                Contract.Requires(stream != null);
                Contract.Requires(hasher != null);
                Contract.Requires(useParallelHashing || parallelHashingFileSizeBoundary == -1);

                _baseStream = stream;
                _hasher = hasher;
                _streamMode = mode;
                _useParallelHashing = useParallelHashing;
                _parallelHashingFileSizeBoundary = parallelHashingFileSizeBoundary;

                if (_useParallelHashing)
                {
                    _hashingBufferBlock = new ActionBlock<Pool<Buffer>.PoolHandle>(
                        HashSegmentAsync,
                        new ExecutionDataflowBlockOptions() { MaxDegreeOfParallelism = 1 });
                }

                _hasherHandle = hasher.CreateToken();
                _hashAlgorithm = _hasherHandle.Hasher;

                if (!_hashAlgorithm.CanTransformMultipleBlocks)
                {
                    throw new NotImplementedException();
                }

                if (_hashAlgorithm.InputBlockSize != 1)
                {
                    throw new NotImplementedException();
                }

                if (_hashAlgorithm.OutputBlockSize != 1)
                {
                    throw new NotImplementedException();
                }
            }

            private void HashSegmentAsync(Pool<Buffer>.PoolHandle handle)
            {
                using (handle)
                {
                    var segment = handle.Value;
                    TransformBlock(segment.Data, 0, segment.Count, null, 0);
                }
            }

            /// <inheritdoc />
            public override bool CanRead => _baseStream.CanRead;

            /// <inheritdoc />
            public override bool CanSeek => _baseStream.CanSeek;

            /// <inheritdoc />
            public override bool CanWrite => _baseStream.CanWrite;

            /// <inheritdoc />
            public override long Length => _baseStream.Length;

            /// <inheritdoc />
            protected override void Dispose(bool disposing)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                if (disposing && !_finalized)
                {
                    FinishHash();
                }

                if (disposing)
                {
                    // Disposing the owning resources only during disposal and not during the finalization.
                    _hasherHandle.Dispose();
                }

                Interlocked.Increment(ref _hasher._calls);
            }

            /// <inheritdoc />
            public override ContentHash GetContentHash()
            {
                if (!_finalized)
                {
                    FinishHash();
                }

                return new ContentHash(_hasher.Info.HashType, _hashAlgorithm.Hash);
            }

            private void FinishHash()
            {
                if (_useParallelHashing)
                {
                    _hashingBufferBlock.Complete();

                    _hashingBufferBlock.Completion.GetAwaiter().GetResult();
                }

                _hashAlgorithm.TransformFinalBlock(EmptyByteArray, 0, 0);

                _finalized = true;
            }

            /// <inheritdoc />
            public override void Flush()
            {
                ThrowIfDisposed();
                _baseStream.Flush();
            }

            /// <inheritdoc />
            public override long Seek(long offset, SeekOrigin origin)
            {
                ThrowIfDisposed();
                return _baseStream.Seek(offset, origin);
            }

            /// <inheritdoc />
            public override void SetLength(long value)
            {
                ThrowIfDisposed();
                _baseStream.SetLength(value);
            }

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count)
            {
                ThrowIfDisposed();
                var bytesRead = _baseStream.Read(buffer, offset, count);

                if (_streamMode == CryptoStreamMode.Read)
                {
                    PushHashBufferAsync(buffer, offset, bytesRead).GetAwaiter().GetResult();
                }

                return bytesRead;
            }

            /// <inheritdoc />
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                ThrowIfDisposed();
                var bytesRead = await _baseStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);

                if (_streamMode == CryptoStreamMode.Read)
                {
                    await PushHashBufferAsync(buffer, offset, bytesRead);
                }

                return bytesRead;
            }

            /// <inheritdoc />
            public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                throw new NotImplementedException();
            }

            /// <inheritdoc />
            public override int EndRead(IAsyncResult asyncResult)
            {
                throw new NotImplementedException();
            }

            private Task<bool> PushHashBufferAsync(byte[] buffer, int offset, int count)
            {
                // In some cases, count can be 0, and in this case we can do nothing and just return a completed task.
                if (count == 0)
                {
                    return TrueTask;
                }

                long foundBytesHashed = Interlocked.Add(ref _bytesHashed, count);

                if (_useParallelHashing && foundBytesHashed > _parallelHashingFileSizeBoundary)
                {
                    var handle = Buffer.GetBuffer();
                    handle.Value.CopyFrom(buffer, offset, count);

                    return _hashingBufferBlock.SendAsync(handle);
                }
                else
                {
                    TransformBlock(buffer, offset, count, null, 0);
                    return TrueTask;
                }
            }

            private void TransformBlock(byte[] buffer, int offset, int count, byte[] outputBuffer, int outputOffset)
            {
                var start = Timer.Elapsed;
                _hashAlgorithm.TransformBlock(buffer, offset, count, outputBuffer, outputOffset);
                var elapsed = Timer.Elapsed - start;
                Interlocked.Add(ref _ticksSpentHashing, elapsed.Ticks);
            }

            /// <inheritdoc />
            public override void Write(byte[] buffer, int offset, int count)
            {
                ThrowIfDisposed();
                _baseStream.Write(buffer, offset, count);

                if (_streamMode == CryptoStreamMode.Write)
                {
                    PushHashBufferAsync(buffer, offset, count).GetAwaiter().GetResult();
                }
            }

            /// <inheritdoc />
            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                ThrowIfDisposed();
                await _baseStream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);

                if (_streamMode == CryptoStreamMode.Write)
                {
                    await PushHashBufferAsync(buffer, offset, count);
                }
            }

            /// <inheritdoc />
            public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                throw new NotImplementedException();
            }

            /// <inheritdoc />
            public override void EndWrite(IAsyncResult asyncResult)
            {
                throw new NotImplementedException();
            }

            /// <inheritdoc />
            public override long Position
            {
                get => _baseStream.Position;
                set => _baseStream.Position = value;
            }

            /// <inheritdoc />
            public override TimeSpan TimeSpentHashing => TimeSpan.FromTicks(_ticksSpentHashing);

            private void ThrowIfDisposed()
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(HashingStreamImpl));
                }
            }

            private sealed class Buffer
            {
                private static readonly Pool<Buffer> BufferPool = new Pool<Buffer>(() => new Buffer());

                public byte[] Data { get; private set; } = new byte[0];

                public int Count { get; private set; } = 0;

                public static Pool<Buffer>.PoolHandle GetBuffer() => BufferPool.Get();

                public void CopyFrom(byte[] buffer, int offset, int count)
                {
                    var array = Data;
                    if (array.Length < count)
                    {
                        Array.Resize(ref array, count);
                    }

                    Array.Copy(buffer, offset, array, 0, count);
                    Data = array;
                    Count = count;
                }
            }
        }
    }
}
