// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Synchronization;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Synchronization
{
    public class LockSetTests : TestBase
    {
        private const int FiveMinuteTimeoutMs = 1000 * 60 * 5;
        private readonly LockSet<string> _lockSet = new LockSet<string>();
        private readonly string[] _keys = {"key2", "key3", "key1"};
        private readonly string[] _keys2 = {"key6", "key5", "key4"};
        private readonly string[] _keysReverse = {"key1", "key3", "key2"};

        public LockSetTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public void DoubleDisposeShouldNotFailForLockHandle()
        {
            var handle = _lockSet.TryAcquire("key1");
            handle.Should().NotBeNull();
            handle.Value.Dispose();
            // Dispose pattern implies that double dispose should never fail.
            handle.Value.Dispose();
        }

        [Fact]
        public void DoubleDisposeShouldNotFailForLockHandleList()
        {
            var handles = _lockSet.AcquireAsync(new []{"key1", "key2"});
            handles.Dispose();
            // Dispose pattern implies that double dispose should never fail.
            handles.Dispose();
        }

        [Fact]
        public async Task Acquire()
        {
            using (await _lockSet.AcquireAsync("key1"))
            {
            }
        }

        [Fact]
        [SuppressMessage("AsyncUsage", "AsyncFixer02:AwaitShouldBeUsedInsteadOfSyncTaskWait")]
        public async Task AcquireBlocksAnotherAcquireOfSameKey()
        {
            Task secondAcquire;

            using (await _lockSet.AcquireAsync("key1"))
            {
                secondAcquire = Task.Run(async () =>
                {
                    using (await _lockSet.AcquireAsync("key1"))
                    {
                    }
                });
                Assert.False(secondAcquire.Wait(50));
            }

            await secondAcquire;
        }

        [Fact]
        [SuppressMessage("AsyncUsage", "AsyncFixer02:AwaitShouldBeUsedInsteadOfSyncTaskWait")]
        public async Task AcquireDoesNotBlockAnotherAcquireOfDifferKey()
        {
            using (await _lockSet.AcquireAsync("key1"))
            {
                Task secondAcquire = Task.Run(async () =>
                {
                    using (await _lockSet.AcquireAsync("key2"))
                    {
                    }
                });

                Assert.True(secondAcquire.Wait(FiveMinuteTimeoutMs));
            }
        }

        [Fact]
        public async Task DifferentKeysHaveDifferentHandles()
        {
            using (var handle1 = await _lockSet.AcquireAsync("key1"))
            {
                using (var handle2 = await _lockSet.AcquireAsync("key2"))
                {
                    Assert.NotEqual(handle2, handle1);
                    Assert.NotEqual(handle2.GetHashCode(), handle1.GetHashCode());
                }
            }
        }

        [Fact]
        public async Task EqualsGivesFalseForOtherType()
        {
            using (var handle = await _lockSet.AcquireAsync("key1"))
            {
                handle.Equals(new object()).Should().BeFalse();
            }
        }

        [Fact]
        public async Task AcquireMultiple()
        {
            using (await _lockSet.AcquireAsync(_keys))
            {
            }
        }

        [Fact]
        [SuppressMessage("AsyncUsage", "AsyncFixer02:AwaitShouldBeUsedInsteadOfSyncTaskWait")]
        public async Task AcquireMultipleBlocksAnotherWithKeyOverlap()
        {
            Task secondAcquire;

            using (await _lockSet.AcquireAsync(_keys))
            {
                secondAcquire = Task.Run(async () =>
                {
                    using (await _lockSet.AcquireAsync(_keysReverse))
                    {
                    }
                });
                secondAcquire.Wait(50).Should().BeFalse();
            }

            await secondAcquire;
        }

        [Fact]
        [SuppressMessage("AsyncUsage", "AsyncFixer02:AwaitShouldBeUsedInsteadOfSyncTaskWait")]
        public async Task AcquireMultipleDoesNotBlockAnotherWithoutKeyOverlap()
        {
            using (await _lockSet.AcquireAsync(_keys))
            {
                var secondAcquire = Task.Run(async () =>
                {
                    using (await _lockSet.AcquireAsync(_keys2))
                    {
                    }
                });
                secondAcquire.Wait(FiveMinuteTimeoutMs).Should().BeTrue();
            }
        }

        [Fact]
        public void AcquireImmediately()
        {
            var lockSet = new LockSet<string>();
            using (var handle = lockSet.TryAcquire("key1"))
            {
                handle.Should().NotBeNull();
            }
        }

        [Fact]
        public async Task AcquireImmediatelyNotBlockedAndReturnsNullWhenKeyHeld()
        {
            var lockSet = new LockSet<string>();
            using (await lockSet.AcquireAsync("key1"))
            {
                using (var handle = lockSet.TryAcquire("key1"))
                {
                    handle.Should().BeNull();
                }
            }
        }
    }
}
