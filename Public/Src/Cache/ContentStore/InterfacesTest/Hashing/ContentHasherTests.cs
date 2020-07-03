// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tasks;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public abstract class ContentHasherTests<T>
        where T : HashAlgorithm, new()
    {
        private readonly HashInfo _hashInfo;
        private readonly byte[] _emptyByteArray = { };

        protected ContentHasherTests()
        {
            _hashInfo = GetBuiltInHashInfo(typeof(T));
        }

        private static HashInfo GetBuiltInHashInfo(Type algorithmType)
        {
            if (algorithmType == typeof(SHA1Managed))
            {
                return SHA1HashInfo.Instance;
            }

            if (algorithmType == typeof(SHA256Managed))
            {
                return SHA256HashInfo.Instance;
            }

#if NET_FRAMEWORK
            if (algorithmType == typeof(MD5Cng))
#else
            if (algorithmType == typeof(MD5CryptoServiceProvider))
#endif
            {
                return MD5HashInfo.Instance;
            }

            if (algorithmType == typeof(VsoHashAlgorithm))
            {
                return VsoHashInfo.Instance;
            }

            if (algorithmType == typeof(DedupChunkHashAlgorithm))
            {
                return DedupChunkHashInfo.Instance;
            }

            if (algorithmType == typeof(DedupNodeHashAlgorithm))
            {
                return DedupNodeHashInfo.Instance;
            }

            throw new ArgumentException(algorithmType.FullName + " is not a built-in hash type.");
        }

        [Fact]
        public void CreatesHashWithCorrectType()
        {
            TestHasher(hasher => Assert.Equal(_hashInfo.HashType, hasher.GetContentHash(_emptyByteArray).HashType));
        }

        [Fact]
        public void CreatesHashFromBytesWithCorrectLength()
        {
            TestHasher(hasher => Assert.Equal(_hashInfo.ByteLength, hasher.GetContentHash(_emptyByteArray).ByteLength));
        }

        [Fact]
        public void CreatesHashFromBytesWithCorrectValue()
        {
            TestHasher(hasher => Assert.Equal(hasher.Info.EmptyHash, hasher.GetContentHash(_emptyByteArray)));
        }

        [Fact]
        public Task CreatesHashFromStreamWithCorrectLength()
        {
            return TestHasherAsync(async hasher =>
            {
                using (var stream = new MemoryStream(_emptyByteArray))
                {
                    var contentHash = await hasher.GetContentHashAsync(stream);
                    Assert.Equal(_hashInfo.ByteLength, contentHash.ByteLength);
                }
            });
        }

        [Fact]
        public Task CreatesHashFromStreamWithCorrectValue()
        {
            return TestHasherAsync(async hasher =>
            {
                using (var stream = new MemoryStream(_emptyByteArray))
                {
                    var contentHash = await hasher.GetContentHashAsync(stream);
                    Assert.Equal(hasher.Info.EmptyHash, contentHash);
                }
            });
        }

        [Fact]
        public void HashBytes()
        {
            var content = ThreadSafeRandom.GetBytes(100);
            TestHasher(hasher =>
            {
                var contentHasher = hasher as IContentHasher;
                var h1 = contentHasher.GetContentHash(content);
                var h2 = contentHasher.GetContentHash(content, 0, content.Length);
                Assert.Equal(h1, h2);
            });
        }

        [Fact]
        public void HasherTokensReturnDifferentHashAlgorithms()
        {
            TestHasher(hasher =>
            {
                using (HasherToken hasherToken1 = hasher.CreateToken())
                {
                    using (HasherToken hasherToken2 = hasher.CreateToken())
                    {
                        Assert.False(hasherToken1.Equals(hasherToken2));
                    }
                }
            });
        }

        [Fact]
        public void HasherTokensReturnToBagWhenDisposed()
        {
            TestHasher(hasher =>
            {
                var disposables = new List<IDisposable>();

                try
                {
                    // First empty the pool
                    while (hasher.PoolSize > 0)
                    {
                        disposables.Add(hasher.CreateToken());
                    }

                    using (hasher.CreateToken())
                    {
                        using (hasher.CreateToken())
                        {
                            Assert.Equal(0, hasher.PoolSize);
                        }
                        Assert.Equal(1, hasher.PoolSize);
                    }
                    Assert.Equal(2, hasher.PoolSize);
                }
                finally
                {
                    foreach (var disposable in disposables)
                    {
                        disposable.Dispose();
                    }
                }
            });
        }

        [Fact]
        public void HasherTokensAreReusedFromBag()
        {
            TestHasher(hasher =>
            {
                using (hasher.CreateToken())
                {
                    HasherToken oldToken;
                    using (HasherToken secondToken = hasher.CreateToken())
                    {
                        oldToken = secondToken;
                    }

                    using (HasherToken secondToken = hasher.CreateToken())
                    {
                        Assert.Equal(oldToken, secondToken);
                    }
                }
            });
        }

        [Fact]
        public void ConcurrentHashing()
        {
            var content = ThreadSafeRandom.GetBytes(1024);
            var hashers = Enumerable.Range(0, 25).Select(i => _hashInfo.CreateContentHasher()).ToList();
            var expectedContentHash = hashers.First().GetContentHash(content);

            Parallel.ForEach(hashers, hasher =>
            {
                var contentHash = hasher.GetContentHash(content);
                Assert.Equal(expectedContentHash, contentHash);
            });
        }

        [Theory]
        [InlineData(-1, false)]
        [InlineData(-1, true)]
        [InlineData(50, false)]
        [InlineData(50, true)]
        [InlineData(1000, false)]
        [InlineData(1000, true)]
        public void ReadHashingStream(int concurrentHashBoundary, bool hasSize)
        {
            foreach (int size in new int[] { 100, 100000 })
            {
                TestHasher(
                    hasher =>
                    {
                        var content = ThreadSafeRandom.GetBytes(size);

                        using (var sourceStream = new MemoryStream(content))
                        using (var hashingSourceStream = hasher.CreateReadHashingStream(hasSize ? (long)size : -1, sourceStream, concurrentHashBoundary))
                        using (var destinationSream = new MemoryStream())
                        {
                            hashingSourceStream.CopyTo(destinationSream);
                            Assert.Equal(hashingSourceStream.GetContentHash(), content.CalculateHash(hasher.Info.HashType));
                            Assert.Equal(hasher.GetContentHash(content), hashingSourceStream.GetContentHash());
                        }
                    });
            }
        }

        [Theory]
        [InlineData(-1, false)]
        [InlineData(-1, true)]
        [InlineData(50, false)]
        [InlineData(50, true)]
        [InlineData(1000000, false)]
        [InlineData(1000000, true)]
        public void WriteHashingStream(int concurrentHashBoundary, bool hasSize)
        {
            foreach (int size in new int[] { 100, 100000 })
            {
                TestHasher(
                    hasher =>
                    {
                        var content = ThreadSafeRandom.GetBytes(size);

                        using (var sourceStream = new MemoryStream(content))
                        using (var destinationStream = new MemoryStream())
                        using (var hashingDestinationStream = hasher.CreateWriteHashingStream(hasSize ? (long)size : -1, destinationStream, concurrentHashBoundary))
                        {
                            sourceStream.CopyTo(hashingDestinationStream);
                            Assert.Equal(hashingDestinationStream.GetContentHash(), content.CalculateHash(hasher.Info.HashType));
                        }
                    });
            }
        }

        private async Task TestHasherAsync(Func<ContentHasher<T>, Task> funcAsync)
        {
            using (var hasher = _hashInfo.CreateContentHasher() as ContentHasher<T>)
            {
                Assert.NotNull(hasher);
                await funcAsync(hasher);
            }
        }

        private void TestHasher(Action<ContentHasher<T>> action)
        {
            using (var hasher = _hashInfo.CreateContentHasher() as ContentHasher<T>)
            {
                Assert.NotNull(hasher);
                action(hasher);
            }
        }

        /// <summary>
        /// This test is to simulate possible corruption of pooled HashAlgorithm which can happen when a canceled copy operation
        /// causes a pending write to execute after a hashing stream is disposed.
        /// </summary>
        [Fact]
        [SuppressMessage("AsyncUsage", "AsyncFixer04:DisposableObjectUsedInFireForgetAsyncCall")]
        public Task TestHasherNotCorruptedByDelayedWrite()
        {
            return TestHasherAsync(
                async hasher =>
                {
                    var content = ThreadSafeRandom.GetBytes(1000);

                    HashAlgorithm algorithm;
                    using (var token = hasher.CreateToken())
                    {
                        // Capture hash algorithm from pool

                        // WHAT???????
                        algorithm = token.Hasher;
                    }

                    if (algorithm is IHashAlgorithmInputLength setInputLength)
                    {
                        setInputLength.SetInputLength(content.Length);
                    }
                    
                    var startTaskSource = TaskSourceSlim.Create<bool>();
                    var completeTaskSource = TaskSourceSlim.Create<bool>();

                    Task writeTask = null;

                    using (var destinationStream = new PausedMemoryStream(startTaskSource, completeTaskSource))
                    using (var hashingDestinationStream = hasher.CreateWriteHashingStream(content.Length, destinationStream))
                    {
                        writeTask = hashingDestinationStream.WriteAsync(content, 0, content.Length);

                        // Wait for write operation to reach inner paused stream
                        await startTaskSource.Task;
                    }

                    // Stream is now disposed (meaning hasher is returned to pool)
                    // Allow write operation to complete which could attempt to write to the underlying hash algorithm
                    // if the operation is not properly guarded
                    completeTaskSource.SetResult(true);

                    // Wait for write task to complete
                    await writeTask;

                    // This should be the same HashAlgorithm which was used by the hashing stream
                    // in the pool. We detect if the HashAlgorithm is corrupted by attempting to hash with
                    // empty content which should give the empty hash.
                    if (algorithm is IHashAlgorithmInputLength setInputLength2)
                    {
                        setInputLength2.SetInputLength(0);
                    }
                    var hash = algorithm.ComputeHash(new byte[0], 0, 0);
                    Assert.Equal(hasher.Info.EmptyHash, new ContentHash(hasher.Info.HashType, hash));
                });
        }

        private class PausedMemoryStream : MemoryStream
        {
            public TaskSourceSlim<bool> StartTaskSource { get; }
            public TaskSourceSlim<bool> CompleteTaskSource { get; }

            public PausedMemoryStream(TaskSourceSlim<bool> startTaskSource, TaskSourceSlim<bool> completeTaskSource)
            {
                StartTaskSource = startTaskSource;
                CompleteTaskSource = completeTaskSource;
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                StartTaskSource.SetResult(true);

                // Don't actually write. Need to ensure no exceptions are thrown since we continue this write after dispose is called.
                return CompleteTaskSource.Task;
            }
        }
    }
}
