// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class ObjectPoolTests : XunitBuildXLTest
    {
        public ObjectPoolTests(ITestOutputHelper output)
            : base(output) { }

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
