// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Defines the behavioral tests shared by both cache implementations. Xunit runs inherited facts once for
    /// each concrete derived test class, whose factory adapts the corresponding cache to the test-only interface.
    /// This keeps the production caches on direct, concrete calls while avoiding duplicate test bodies.
    /// </summary>
    public abstract class ObjectCacheTestsBase : XunitBuildXLTest
    {
        private struct HashedKey : IEquatable<HashedKey>
        {
            public int Key;
            public int Hash;

            public bool Equals(HashedKey other)
            {
                return other.Key == Key;
            }

            public override int GetHashCode()
            {
                return Hash;
            }
        }

        private struct CacheValue
        {
            public long First;
            public long Second;
        }

        protected ObjectCacheTestsBase(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void TestCacheNotPresent()
        {
            var cache = CreateCache<HashedKey, int>(16);
            int value;
            XAssert.IsFalse(cache.TryGetValue(default(HashedKey), out value));
            XAssert.IsFalse(cache.TryGetValue(new HashedKey { Hash = int.MaxValue }, out value));
        }

        [Fact]
        public void TestCacheAddGet()
        {
            var cache = CreateCache<HashedKey, int>(16);

            long hits = 0;
            long misses = 0;

            // Loop twice. We know that cache should still return
            // false on first TryGetValue because the entries must have
            // been evicted by the end the time the same index is reached twice
            for (int j = 0; j < 2; j++)
            {
                // Skip zero since table cache will find HashedKey with key = 0 and hash = 0.
                // since this is equal to default(HashedKey).
                for (int i = 1; i < 64; i++)
                {
                    var key = new HashedKey()
                    {
                        Key = i,
                        Hash = i
                    };

                    int value;
                    XAssert.IsFalse(cache.TryGetValue(key, out value));
                    cache.AddItem(key, i);

                    misses++;
                    XAssert.AreEqual(hits, cache.Hits);
                    XAssert.AreEqual(misses, cache.Misses);

                    XAssert.IsTrue(cache.TryGetValue(key, out value));
                    XAssert.AreEqual(i, value);

                    hits++;
                    XAssert.AreEqual(hits, cache.Hits);
                    XAssert.AreEqual(misses, cache.Misses);
                }
            }
        }

        [Fact]
        public void TestParallelCacheAddGet()
        {
            var cache = CreateCache<HashedKey, int>(16);
            var random = new Random(0);

            var expectedValues = new int[16 * 1024];
            for (int i = 0; i < expectedValues.Length; i++)
            {
                // Use some arbitrarily chose sampling sets to
                // give a test of interspersed hits and misses
                if ((i % 9) == 0)
                {
                    expectedValues[i] = random.Next(-5, 5);
                }
                else if ((i % 7) == 0)
                {
                    expectedValues[i] = random.Next(16, 32);
                }
                else
                {
                    expectedValues[i] = random.Next(-64, 64);
                }
            }

            // Skip zero since table cache will find HashedKey with key = 0 and hash = 0.
            // since this is equal to default(HashedKey).
            Parallel.For(
                1,
                expectedValues.Length,
                i =>
                {
                    int expectedValue = expectedValues[i];

                    var key = new HashedKey()
                    {
                        Key = expectedValue,
                        Hash = expectedValue
                    };

                    int value;
                    if (!cache.TryGetValue(key, out value))
                    {
                        cache.AddItem(key, expectedValue);
                    }
                    else
                    {
                        XAssert.AreEqual(expectedValue, value);
                    }
                });
        }

        [Fact]
        public void TestCustomComparer()
        {
            var cache = CreateCache<string, int>(17, StringComparer.OrdinalIgnoreCase);
            cache.AddItem("key", 42);

            XAssert.IsTrue(cache.TryGetValue("KEY", out int value));
            XAssert.AreEqual(42, value);
        }

        [Fact]
        public void TestCacheDoesNotReturnTornEntries()
        {
            var cache = CreateCache<HashedKey, CacheValue>(17);

            Parallel.For(
                1,
                100_000,
                i =>
                {
                    int keyValue = (i % 2_000) + 1;
                    var key = new HashedKey { Key = keyValue, Hash = keyValue % 17 };
                    if (!cache.TryGetValue(key, out CacheValue value))
                    {
                        value = new CacheValue { First = keyValue, Second = ~keyValue };
                        cache.AddItem(key, value);
                    }

                    XAssert.AreEqual(keyValue, value.First);
                    XAssert.AreEqual(~keyValue, value.Second);
                });

            XAssert.IsTrue(cache.Hits + cache.Misses > 0);
            XAssert.IsTrue(cache.Hits + cache.Misses <= 99_999);
        }

        protected abstract ITestCache<TKey, TValue> CreateCache<TKey, TValue>(
            int capacity,
            IEqualityComparer<TKey> comparer = null);

        /// <summary>
        /// Represents only the API common to both caches. Implementation-specific APIs are tested on the
        /// corresponding concrete test class instead of being forced into the shared abstraction.
        /// </summary>
        protected interface ITestCache<TKey, TValue>
        {
            long Hits { get; }

            long Misses { get; }

            bool TryGetValue(TKey key, out TValue value);

            bool AddItem(TKey key, TValue value);
        }
    }

    public sealed class ObjectCacheTests : ObjectCacheTestsBase
    {
        public ObjectCacheTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestClear()
        {
            var cache = new ObjectCache<int, string>(17);
            cache.AddItem(1, "one");
            XAssert.IsTrue(cache.TryGetValue(1, out _));

            cache.Clear();

            XAssert.IsFalse(cache.TryGetValue(1, out _));
        }

        protected override ITestCache<TKey, TValue> CreateCache<TKey, TValue>(
            int capacity,
            IEqualityComparer<TKey> comparer = null)
        {
            return new LockedCacheAdapter<TKey, TValue>(capacity, comparer);
        }

        private sealed class LockedCacheAdapter<TKey, TValue> : ITestCache<TKey, TValue>
        {
            private readonly ObjectCache<TKey, TValue> m_cache;

            public LockedCacheAdapter(int capacity, IEqualityComparer<TKey> comparer)
            {
                m_cache = new ObjectCache<TKey, TValue>(capacity, comparer);
            }

            public long Hits => m_cache.Hits;

            public long Misses => m_cache.Misses;

            public bool TryGetValue(TKey key, out TValue value) => m_cache.TryGetValue(key, out value);

            public bool AddItem(TKey key, TValue value) => m_cache.AddItem(key, value);
        }
    }

    public sealed class LockFreeObjectCacheTests : ObjectCacheTestsBase
    {
        public LockFreeObjectCacheTests(ITestOutputHelper output)
            : base(output)
        {
        }

        protected override ITestCache<TKey, TValue> CreateCache<TKey, TValue>(
            int capacity,
            IEqualityComparer<TKey> comparer = null)
        {
            return new LockFreeCacheAdapter<TKey, TValue>(capacity, comparer);
        }

        [Fact]
        public void TestGetOrAdd()
        {
            var cache = new LockFreeObjectCache<int, int>(16);
            int factoryCalls = 0;

            int first = cache.GetOrAdd(1, 42, (_, value) =>
            {
                factoryCalls++;
                return value;
            });
            int second = cache.GetOrAdd(1, 43, (_, value) =>
            {
                factoryCalls++;
                return value;
            });

            XAssert.AreEqual(42, first);
            XAssert.AreEqual(42, second);
            XAssert.AreEqual(1, factoryCalls);
            XAssert.AreEqual(1, cache.Hits);
            XAssert.AreEqual(1, cache.Misses);
        }

        private sealed class LockFreeCacheAdapter<TKey, TValue> : ITestCache<TKey, TValue>
        {
            private readonly LockFreeObjectCache<TKey, TValue> m_cache;

            public LockFreeCacheAdapter(int capacity, IEqualityComparer<TKey> comparer)
            {
                m_cache = new LockFreeObjectCache<TKey, TValue>(capacity, comparer);
            }

            public long Hits => m_cache.Hits;

            public long Misses => m_cache.Misses;

            public bool TryGetValue(TKey key, out TValue value) => m_cache.TryGetValue(key, out value);

            public bool AddItem(TKey key, TValue value) => m_cache.AddItem(key, value);
        }
    }
}
