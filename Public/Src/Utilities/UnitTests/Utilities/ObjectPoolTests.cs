// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public sealed class ObjectPoolTests : XunitBuildXLTest
    {
        public ObjectPoolTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void AllPublicPoolsAreRegisteredForMemoryConservation()
        {
            var publicPools = typeof(Pools)
                .GetMembers(BindingFlags.Public | BindingFlags.Static)
                .Select(member => (member.Name, Pool: GetObjectPool(member)))
                .Where(entry => entry.Pool != null)
                .ToList();

            Assert.NotEmpty(publicPools);

            var memoryConservation = new MemoryConservation();
            Pools.RegisterMemoryConservationTargets(memoryConservation);

            foreach (var (name, pool) in publicPools)
            {
                Assert.True(
                    memoryConservation.IsRegisteredForTesting((IMemoryConservationTarget)pool),
                    $"Pools.{name} is not registered by {nameof(Pools.RegisterMemoryConservationTargets)}.");
            }
        }

        [Fact]
        public void MemoryStreamPoolTests()
        {
            useMemoryStreamFromPool();

            // the length and the position of an instance obtained from the pool should always be 0.
            using var wrapper = Pools.MemoryStreamPool.GetInstance();
            Assert.Equal(0, wrapper.Instance.ToArray().Length);
            Assert.Equal(0, wrapper.Instance.Length);
            Assert.Equal(0, wrapper.Instance.Position);

            static void useMemoryStreamFromPool()
            {
                using var wrapper = Pools.MemoryStreamPool.GetInstance();
                wrapper.Instance.WriteByte(42);
            }
        }

        private static object GetObjectPool(MemberInfo member)
        {
            Type memberType;
            object value;

            if (member is FieldInfo field)
            {
                memberType = field.FieldType;
                value = field.GetValue(null);
            }
            else if (member is PropertyInfo property && property.GetIndexParameters().Length == 0)
            {
                memberType = property.PropertyType;
                value = property.GetValue(null);
            }
            else
            {
                return null;
            }

            return memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(ObjectPool<>)
                ? value
                : null;
        }

        [Fact]
        public void RetainedSizePolicyCleansNormalObjectsAndDiscardsOversizedObjects()
        {
            int cleanupCount = 0;
            var pool = new ObjectPool<StringBuilder>(
                creator: () => new StringBuilder(),
                cleanup: builder => { cleanupCount++; builder.Clear(); },
                sizeProvider: builder => builder.Length,
                maximumRetainedSize: 4);
            StringBuilder retainedBuilder;

            using (var wrapper = pool.GetInstance())
            {
                retainedBuilder = wrapper.Instance;
                retainedBuilder.Append("1234");
            }

            using (var wrapper = pool.GetInstance())
            {
                Assert.Same(retainedBuilder, wrapper.Instance);
                Assert.Equal(0, wrapper.Instance.Length);
                wrapper.Instance.Append("12345");
            }

            Assert.Equal(1, cleanupCount);
            Assert.Equal(1, pool.OversizedObjectCount);
            Assert.Equal(0, pool.ObjectsInPool);

            using var replacementWrapper = pool.GetInstance();
            Assert.NotSame(retainedBuilder, replacementWrapper.Instance);
        }

        [Fact]
        public void RetainedSizePolicyUsesDefaultMaximum()
        {
            var pool = new ObjectPool<StringBuilder>(
                creator: () => new StringBuilder(),
                cleanup: builder => builder.Clear(),
                sizeProvider: builder => builder.Capacity);
            StringBuilder oversizedBuilder;

            using (var wrapper = pool.GetInstance())
            {
                oversizedBuilder = wrapper.Instance;
                oversizedBuilder.EnsureCapacity(ObjectPool<StringBuilder>.DefaultMaximumRetainedSize + 1);
            }

            Assert.Equal(1, pool.OversizedObjectCount);

            using var replacementWrapper = pool.GetInstance();
            Assert.NotSame(oversizedBuilder, replacementWrapper.Instance);
        }

        [Fact]
        public void MemoryStreamPoolDoesNotRetainOversizedStreams()
        {
            long priorOversizedObjectCount = Pools.MemoryStreamPool.OversizedObjectCount;
            MemoryStream oversizedStream;

            using (var wrapper = Pools.MemoryStreamPool.GetInstance())
            {
                oversizedStream = wrapper.Instance;
                oversizedStream.Capacity = Pools.MaximumMemoryStreamCapacityToRetain + 1;
            }

            Assert.Equal(priorOversizedObjectCount + 1, Pools.MemoryStreamPool.OversizedObjectCount);

            using var replacementWrapper = Pools.MemoryStreamPool.GetInstance();
            Assert.NotSame(oversizedStream, replacementWrapper.Instance);
        }

        [Fact]
        public void BoundedListPoolDoesNotRetainOversizedLists()
        {
            var pool = Pools.CreateListPool<int>(maximumCapacityToRetain: 4);
            List<int> oversizedList;

            using (var wrapper = pool.GetInstance())
            {
                oversizedList = wrapper.Instance;
                oversizedList.Capacity = 5;
            }

            Assert.Equal(1, pool.OversizedObjectCount);
            Assert.Equal(0, pool.ObjectsInPool);

            using var cleanedWrapper = pool.GetInstance();
            Assert.NotSame(oversizedList, cleanedWrapper.Instance);
            Assert.Equal(0, cleanedWrapper.Instance.Capacity);
        }

        [Fact]
        public void BoundedSetPoolDoesNotRetainOversizedSetsAndPreservesComparer()
        {
            var pool = Pools.CreateSetPool<string>(maximumSetSizeToRetain: 4, comparer: StringComparer.OrdinalIgnoreCase);
            HashSet<string> oversizedSet;

            using (var wrapper = pool.GetInstance())
            {
                oversizedSet = wrapper.Instance;
                oversizedSet.UnionWith(new[] { "one", "two", "three", "four", "five" });
            }

            using var cleanedWrapper = pool.GetInstance();
            Assert.NotSame(oversizedSet, cleanedWrapper.Instance);
            Assert.Equal(1, pool.OversizedObjectCount);
            Assert.True(cleanedWrapper.Instance.Add("value"));
            Assert.False(cleanedWrapper.Instance.Add("VALUE"));
        }
        
        [Fact]
        public void BinaryWriterWithPooledMemoryStreamWorksAsExpected()
        {
            useMemoryStreamFromPool();

            using (var pools = Pools.MemoryStreamPool.GetInstance())
            using (var writer = new BinaryWriter(pools.Instance, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(42);
                var data = pools.Instance.ToArray();
                // The length is 4, because of aligning.
                Assert.Equal(4, data.Length); // with the old code the data.Length is 5, not 4
            }

            static void useMemoryStreamFromPool()
            {
                using var wrapper = Pools.MemoryStreamPool.GetInstance();
                wrapper.Instance.WriteByte(42);
                wrapper.Instance.WriteByte(42);
                wrapper.Instance.WriteByte(42);
                wrapper.Instance.WriteByte(42);
                wrapper.Instance.WriteByte(42);
            }
        }

        [Fact]
        public void ObjectPoolWillReturnNewInstanceIfCleanupMethodCreatesNewInstance()
        {
            ObjectPool<StringBuilder> disabledPool = new ObjectPool<StringBuilder>(
                creator: () => new StringBuilder(),
                cleanup: sb => new StringBuilder());

            StringBuilder firstInstanceFromDisabledPool;
            using (var wrap = disabledPool.GetInstance())
            {
                firstInstanceFromDisabledPool = wrap.Instance;
            }

            StringBuilder secondInstanceFromDisabledPool;
            using (var wrap = disabledPool.GetInstance())
            {
                secondInstanceFromDisabledPool = wrap.Instance;
            }

            XAssert.AreNotSame(firstInstanceFromDisabledPool, secondInstanceFromDisabledPool, "Disabled pool should return new instance each time.");

            ObjectPool<StringBuilder> regularPool = new ObjectPool<StringBuilder>(
                creator: () => new StringBuilder(),
                cleanup: sb => sb.Clear());

            StringBuilder firstInstanceFromRegularPool;
            using (var wrap = regularPool.GetInstance())
            {
                firstInstanceFromRegularPool = wrap.Instance;
            }

            StringBuilder secondInstanceFromRegularPool;
            using (var wrap = regularPool.GetInstance())
            {
                secondInstanceFromRegularPool = wrap.Instance;
            }

            XAssert.AreSame(firstInstanceFromRegularPool, secondInstanceFromRegularPool, "Regular pool should return each object every time.");
        }

        [Fact]
        public void ClearResetsPoolSizeAccounting()
        {
            var pool = new ObjectPool<StringBuilder>(
                creator: () => new StringBuilder(),
                cleanup: builder => builder.Clear());
            var wrappers = Enumerable.Range(0, Environment.ProcessorCount * 4 + 1)
                .Select(_ => pool.GetInstance())
                .ToArray();

            foreach (var wrapper in wrappers)
            {
                wrapper.Dispose();
            }

            pool.Clear();
            Assert.Equal(0, pool.ObjectsInPool);

            wrappers = Enumerable.Range(0, Environment.ProcessorCount * 4 + 1)
                .Select(_ => pool.GetInstance())
                .ToArray();

            foreach (var wrapper in wrappers)
            {
                wrapper.Dispose();
            }

            Assert.Equal(Environment.ProcessorCount * 4 + 1, pool.ObjectsInPool);
        }

        [Fact]
        public void PoolDoesNotRetainObjectsDuringMemoryConservation()
        {
            var memoryConservation = new MemoryConservation();
            var pool = new ObjectPool<StringBuilder>(
                creator: () => new StringBuilder(),
                cleanup: builder => builder.Clear());
            memoryConservation.Register(pool);

            var wrapper = pool.GetInstance();
            wrapper.Instance.Append("retained content");
            wrapper.Dispose();
            Assert.Equal(1, pool.ObjectsInPool);

            wrapper = pool.GetInstance();
            memoryConservation.Enter();

            Assert.Equal(0, pool.ObjectsInPool);

            wrapper.Dispose();
            Assert.Equal(0, pool.ObjectsInPool);

            memoryConservation.Exit(force: true);
            wrapper = pool.GetInstance();
            wrapper.Dispose();
            Assert.Equal(1, pool.ObjectsInPool);
        }

        [Fact]
        public void ObjectPools()
        {
            // make sure that the pool returns distinct objects
            using (PooledObjectWrapper<StringBuilder> wrap = Pools.GetStringBuilder())
            {
                StringBuilder sb = wrap.Instance;

                using (PooledObjectWrapper<StringBuilder> wrap2 = Pools.GetStringBuilder())
                {
                    StringBuilder sb2 = wrap2.Instance;

                    XAssert.AreNotSame(sb2, sb);
                }
            }

            // the pool's counts should be at least 2
            XAssert.IsTrue(Pools.StringBuilderPool.ObjectsInPool >= 2);
            XAssert.IsTrue(Pools.StringBuilderPool.UseCount >= 2);

            // try out the core APIs directly
            {
                PooledObjectWrapper<StringBuilder> wrap = Pools.StringBuilderPool.GetInstance();
                Pools.StringBuilderPool.PutInstance(wrap);
            }
        }

        [Fact]
        public void StringBuilderPool()
        {
            using (PooledObjectWrapper<StringBuilder> wrap = Pools.GetStringBuilder())
            {
                StringBuilder sb = wrap.Instance;
                XAssert.AreEqual(0, sb.Length);
                sb.Append("1234");
            }

            // make sure we get back a cleared StringBuilder
            using (PooledObjectWrapper<StringBuilder> wrap = Pools.GetStringBuilder())
            {
                StringBuilder sb = wrap.Instance;
                XAssert.AreEqual(0, sb.Length);
            }
        }

        [Fact]
        public void StringListPool()
        {
            using (PooledObjectWrapper<List<string>> wrap = Pools.GetStringList())
            {
                List<string> l = wrap.Instance;
                XAssert.AreEqual(0, l.Count);
                l.Add("1234");
            }

            // make sure we get back a cleared list
            using (PooledObjectWrapper<List<string>> wrap = Pools.GetStringList())
            {
                List<string> l = wrap.Instance;
                XAssert.AreEqual(0, l.Count);
            }
        }

        [Fact]
        public void NoConcurrentAccess()
        {
            var pool = new ObjectPool<SafeCounter>(
                creator: () => new SafeCounter(),
                cleanup: c => c.Clear());

            var threads = Enumerable.Range(0, 10).Select(i =>
            {
                return new Thread(() =>
                {
                    for (int k = 0; k < 10000; k++)
                    {
                        using (var counterWrapper = pool.GetInstance())
                        {
                            var counterInstance = counterWrapper.Instance;
                            XAssert.AreEqual(0, counterInstance.Count);
                            counterInstance.Inc();
                            counterInstance.Inc();
                            XAssert.AreEqual(2, counterInstance.Count);
                        }
                    }
                });
            }).ToList();

            foreach (var t in threads)
            {
                t.Start();
            }

            foreach (var t in threads)
            {
                t.Join();
            }
        }

        class SafeCounter
        {
            private long m_count = 0;

            public SafeCounter()
            {
            }

            public void Clear() => Interlocked.Exchange(ref m_count, 0);

            public void Inc() => Interlocked.Increment(ref m_count);

            public long Count => Interlocked.Read(ref m_count);
        }

    }
}
