// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class ObjectCacheTests : XunitBuildXLTest
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

        public ObjectCacheTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void TestCacheNotPresent()
        {
            var cache = new ObjectCache<HashedKey, int>(16);
            int value;
            XAssert.IsFalse(cache.TryGetValue(default(HashedKey), out value));
            XAssert.IsFalse(cache.TryGetValue(new HashedKey { Hash = int.MaxValue }, out value));
        }

        [Fact]
        public void TestCacheAddGet()
        {
            var cache = new ObjectCache<HashedKey, int>(16);

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
            var cache = new ObjectCache<HashedKey, int>(17);
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

                    long misses = cache.Misses;
                    long hits = cache.Hits;

                    int value;
                    if (!cache.TryGetValue(key, out value))
                    {
                        XAssert.IsTrue(cache.Misses > misses);
                        cache.AddItem(key, expectedValue);
                    }
                    else
                    {
                        XAssert.IsTrue(cache.Hits > hits);
                        XAssert.AreEqual(expectedValue, value);
                    }
                });
        }
    }
}
