// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.UtilitiesCore;

#pragma warning disable CS3001 // CLS
#pragma warning disable CS3002
#pragma warning disable CS3003

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
        private readonly Pool<HashAlgorithm> _algorithmsPool = new Pool<HashAlgorithm>(() => new T(), maxReserveInstances: HashInfoLookup.ContentHasherIdlePoolSize);

        private static readonly ByteArrayPool _bufferPool = GlobalObjectPools.FileIOBuffersArrayPool;

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
        public HasherToken CreateToken()
        {
            var poolHandle = _algorithmsPool.Get();
            return new HasherToken(poolHandle);
        }

        private HasherToken CreateToken(long expectedLength)
        {
            var poolHandle = _algorithmsPool.Get();
            var result = new HasherToken(poolHandle);
            if (result.Hasher is IHashAlgorithmInputLength sizeHint)
            {
                sizeHint.SetInputLength(expectedLength);
            }

            return result;
        }

        /// <summary>
        /// GetContentHashInternalAsync - for internal use only.
        /// </summary>
        /// <param name="content">Content stream with length.</param>
        /// <returns>A tuple of content hash and dedup node.</returns>
        protected async Task<(ContentHash, DedupNode?)> GetContentHashInternalAsync(StreamWithLength content)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var hasherHandle = CreateToken(expectedLength: content.Length - content.Stream.Position);
                var hasher = hasherHandle.Hasher;

                Pool<byte[]>.PoolHandle bufferHandle;
                if (hasher is IHashAlgorithmBufferPool bufferPool)
                {
                    bufferHandle = bufferPool.GetBufferFromPool();
                }
                else
                {
                    bufferHandle = _bufferPool.Get();
                }

                using (bufferHandle)
                {
                    var buffer = bufferHandle.Value;

                    int bytesJustRead;

                    do
                    {
                        int totalBytesRead = 0;
                        int bytesNeeded = buffer.Length - totalBytesRead;
                        do
                        {
                            bytesJustRead = await content.Stream.ReadAsync(buffer, totalBytesRead, buffer.Length - totalBytesRead).ConfigureAwait(false);
                            totalBytesRead += bytesJustRead;
                            bytesNeeded -= bytesJustRead;
                        } while (bytesNeeded > 0 && bytesJustRead != 0);

                        hasher.TransformBlock(buffer, 0, totalBytesRead, null, 0);
                    } while (bytesJustRead > 0);

                    hasher.TransformFinalBlock(buffer, 0, 0);
                    var hashBytes = hasher.Hash!;

                    // Retrieve the DedupNode before losing the hasher token.
                    switch (Info.HashType)
                    {
                        case HashType.Dedup64K:
                        case HashType.Dedup1024K:
                            var contentHasher = (DedupNodeOrChunkHashAlgorithm)hasher;
                            return (new ContentHash(Info.HashType, hashBytes), contentHasher.GetNode());
                        case HashType.SHA1:
                        case HashType.SHA256:
                        case HashType.MD5:
                        case HashType.Vso0:
                        case HashType.DedupSingleChunk:
                        case HashType.DedupNode:
                        case HashType.Murmur:
                            return (new ContentHash(Info.HashType, hashBytes), null);
                        default:
                            throw new NotImplementedException($"Unsupported hash type: {Info.HashType} encountered when hashing content.");
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
        public async Task<ContentHash> GetContentHashAsync(StreamWithLength content)
        {
            var (contentHash, _) = await GetContentHashInternalAsync(content);
            return contentHash;
        }

#if NET_COREAPP
        /// <inheritdoc />
        public ContentHash GetContentHash(ReadOnlySpan<byte> content)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using (var hasherToken = CreateToken(expectedLength: content.Length))
                {
                    var hasher = hasherToken.Hasher;

                    Span<byte> hashOutput = stackalloc byte[Info.ByteLength];
                    hasher.TryComputeHash(content, hashOutput, out _);

                    return new ContentHash(Info.HashType, hashOutput.ToArray());
                }
            }
            finally
            {
                stopwatch.Stop();
                Interlocked.Add(ref _ticks, stopwatch.Elapsed.Ticks);
                Interlocked.Increment(ref _calls);
            }
        }
#endif //NET_COREAPP

        /// <inheritdoc />
        public ContentHash GetContentHash(byte[] content)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using (var hasherToken = CreateToken(expectedLength: content.Length))
                {
                    var hasher = hasherToken.Hasher;
                    var hashBytes = hasher.ComputeHash(content);
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
                using (var hasherToken = CreateToken(expectedLength: count))
                {
                    var hasher = hasherToken.Hasher;
                    var hashBytes = hasher.ComputeHash(content, offset, count);
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

        /// <inheritdoc />
        public HashingStream CreateReadHashingStream(long streamLength, Stream stream, long parallelHashingFileSizeBoundary = -1)
        {
            return new HashingStreamImpl(stream, this, CryptoStreamMode.Read, useParallelHashing: parallelHashingFileSizeBoundary >= 0, parallelHashingFileSizeBoundary, streamLength);
        }

        /// <inheritdoc />
        public HashingStream CreateReadHashingStream(StreamWithLength stream, long parallelHashingFileSizeBoundary = -1)
        {
            return CreateReadHashingStream(stream.Length, stream, parallelHashingFileSizeBoundary);
        }

        /// <inheritdoc />
        public HashingStream CreateWriteHashingStream(long streamLength, Stream stream, long parallelHashingFileSizeBoundary = -1)
        {
            return new HashingStreamImpl(stream, this, CryptoStreamMode.Write, useParallelHashing: parallelHashingFileSizeBoundary >= 0, parallelHashingFileSizeBoundary, streamLength);
        }

        /// <inheritdoc />
        public HashingStream CreateWriteHashingStream(StreamWithLength stream, long parallelHashingFileSizeBoundary = -1)
        {
            return CreateWriteHashingStream(stream.Length, stream, parallelHashingFileSizeBoundary);
        }

        /// <summary>
        ///     A stream that wraps an existing stream and computes a hash of the bytes that pass through it.
        /// </summary>
        private sealed class HashingStreamImpl : HashingStream
        {
            private static readonly byte[] EmptyByteArray = new byte[0];
            private static readonly Task<bool> TrueTask = Task.FromResult(true);

            private static readonly Stopwatch Timer = Stopwatch.StartNew();

            private readonly ActionBlock<Pool<Buffer>.PoolHandle>? _hashingBufferBlock;

            private readonly Stream _baseStream;
            private readonly ContentHasher<T> _hasher;

            private readonly CryptoStreamMode _streamMode;
            private readonly long _parallelHashingFileSizeBoundary;
            private readonly bool _useParallelHashing;
            private readonly GuardedHashAlgorithm _hashAlgorithm;
            private bool _disposed;

            private long _bytesHashed = 0;

            private long _ticksSpentHashing;

            public HashingStreamImpl(
                Stream stream,
                ContentHasher<T> hasher,
                CryptoStreamMode mode,
                bool useParallelHashing,
                long parallelHashingFileSizeBoundary,
                long streamLength)
            {
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

                _hashAlgorithm = new GuardedHashAlgorithm(this, streamLength, hasher);
            }

            private void HashSegmentAsync(Pool<Buffer>.PoolHandle handle)
            {
                using (handle)
                {
                    var segment = handle.Value;
                    TransformBlock(segment.Data, 0, segment.Count);
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

                if (disposing && !_hashAlgorithm.Finalized)
                {
                    FinishHash();
                }

                if (disposing)
                {
                    // Disposing the owning resources only during disposal and not during the finalization.
                    _hashAlgorithm.Dispose();
                }

                Interlocked.Increment(ref _hasher._calls);
            }

            /// <inheritdoc />
            public override ContentHash GetContentHash()
            {
                if (!_hashAlgorithm.Finalized)
                {
                    FinishHash();
                }

                return new ContentHash(_hasher.Info.HashType, _hashAlgorithm.Hash);
            }

            private void FinishHash()
            {
                if (_useParallelHashing)
                {
                    _hashingBufferBlock!.Complete();
                    _hashingBufferBlock.Completion.GetAwaiter().GetResult();
                }

                _hashAlgorithm.Finish();
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
            public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, [AllowNull]AsyncCallback callback, object? state)
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

                    return _hashingBufferBlock!.SendAsync(handle);
                }
                else
                {
                    TransformBlock(buffer, offset, count);
                    return TrueTask;
                }
            }

            private void TransformBlock(byte[] buffer, int offset, int count)
            {
                var start = Timer.Elapsed;
                _hashAlgorithm.TransformBlock(buffer, offset, count);
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
            public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, [AllowNull]AsyncCallback callback, object? state)
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

            /// <summary>
            /// The class protects accesses to an underlying pooled HashAlgorithm so that once the
            /// HashAlgorithm is returned to the pool, it cannot be corrupted. Without these guards,
            /// the HashAlgorithm can be used after being returned to the pool in the following scenario:
            /// 1. Write is stuck in the base stream WriteAsync.
            /// 2. Write operation is canceled.
            /// 3. Caller disposes stream which returns HashAlgorithm to the pool.
            /// 4. Write operation completes (does not respect cancellation) and continuation writes to the HashAlgorithm.
            /// 
            /// It is also important to note that locking is desirable here to prevent subtle race conditions compared to other schemes
            /// such as setting hashAlgorithm to null on Dispose.
            /// </summary>
            private sealed class GuardedHashAlgorithm : IDisposable
            {
                private readonly HasherToken _hasherToken;
                private readonly HashingStreamImpl _ownerStream;
                public bool Finalized { get; private set; }

                public GuardedHashAlgorithm(HashingStreamImpl ownerStream, long streamLength, ContentHasher<T> hasher)
                {
                    _ownerStream = ownerStream;
                    _hasherToken = hasher.CreateToken();

                    var hashAlgorithm = _hasherToken.Hasher;

                    if (streamLength >= 0 && hashAlgorithm is IHashAlgorithmInputLength sizeHint)
                    {
                        sizeHint.SetInputLength(streamLength);
                    }

                    if (!hashAlgorithm.CanTransformMultipleBlocks)
                    {
                        throw new NotImplementedException();
                    }

                    if (hashAlgorithm.InputBlockSize != 1)
                    {
                        throw new NotImplementedException();
                    }

                    if (hashAlgorithm.OutputBlockSize != 1)
                    {
                        throw new NotImplementedException();
                    }
                }

                public byte[] Hash
                {
                    get
                    {
                        lock (this)
                        {
                            _ownerStream.ThrowIfDisposed();
                            Contract.Assert(Finalized);
                            return _hasherToken.Hasher.Hash!;
                        }
                    }
                }

                public void TransformBlock(byte[] buffer, int offset, int count)
                {
                    lock (this)
                    {
                        if (_ownerStream._disposed || Finalized)
                        {
                            return;
                        }

                        _hasherToken.Hasher.TransformBlock(buffer, offset, count, null, 0);
                    }
                }

                public void Finish()
                {
                    lock (this)
                    {
                        if (_ownerStream._disposed || Finalized)
                        {
                            return;
                        }

                        _hasherToken.Hasher.TransformFinalBlock(EmptyByteArray, 0, 0);
                        Finalized = true;
                    }
                }

                public void Dispose()
                {
                    lock (this)
                    {
                        // Owner stream must be disposed before returning the hash algorithm to pool
                        Contract.Requires(_ownerStream._disposed);
                        _hasherToken.Dispose();
                    }
                }
            }

            private sealed class Buffer
            {
                private static readonly Pool<Buffer> BufferPool = new Pool<Buffer>(() => new Buffer());

                public byte[] Data { get; private set; } = EmptyByteArray;

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
