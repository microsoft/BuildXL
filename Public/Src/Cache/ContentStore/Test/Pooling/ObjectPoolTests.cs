// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Text;
using BuildXL.Cache.ContentStore.Pooling;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Pooling
{
    public sealed class ObjectPoolTests : TestBase
    {
        public ObjectPoolTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public void ObjectPoolWillReturnNewInstanceIfCleanupMethodCreatesNewInstance()
        {
            // ReSharper disable once ObjectCreationAsStatement
            using (ObjectPool<StringBuilder> disabledPool = new ObjectPool<StringBuilder>(
                () => new StringBuilder(), sb => new StringBuilder()))
            {
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

                firstInstanceFromDisabledPool.Should()
                    .NotBeSameAs(secondInstanceFromDisabledPool, "Disabled pool should return new instance each time.");
                using (ObjectPool<StringBuilder> regularPool = new ObjectPool<StringBuilder>(() => new StringBuilder(), sb => sb.Clear()))
                {
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

                    firstInstanceFromRegularPool.Should()
                        .Be(secondInstanceFromRegularPool, "Regular pool should return each object every time.");
                }
            }
        }

        [Fact]
        public void ObjectPools()
        {
            using (var stringBuilderPool = new TestObjectPool<StringBuilder>(() => new StringBuilder(), sb => sb.Clear()))
            {
                // make sure that the pool returns distinct objects
                using (PooledObjectWrapper<StringBuilder> wrap = stringBuilderPool.GetInstance())
                {
                    StringBuilder sb = wrap.Instance;

                    using (PooledObjectWrapper<StringBuilder> wrap2 = stringBuilderPool.GetInstance())
                    {
                        StringBuilder sb2 = wrap2.Instance;

                        Assert.NotEqual(sb2, sb);
                    }
                }

                // the pool's counts should be at least 2
                Assert.True(stringBuilderPool.ObjectsInPool >= 2);
                Assert.True(stringBuilderPool.UseCount >= 2);

                // try out the core APIs directly
                {
                    PooledObjectWrapper<StringBuilder> wrap = stringBuilderPool.GetInstance();
                    stringBuilderPool.PutInstanceForTest(wrap);
                }
            }
        }

        [Fact]
        public void StringBuilderPool()
        {
            using (var stringBuilderPool = new ObjectPool<StringBuilder>(() => new StringBuilder(), sb => sb.Clear()))
            {
                using (PooledObjectWrapper<StringBuilder> wrap = stringBuilderPool.GetInstance())
                {
                    StringBuilder sb = wrap.Instance;
                    sb.Length.Should().Be(0);
                    sb.Append("1234");
                }

                // make sure we get back a cleared StringBuilder
                using (PooledObjectWrapper<StringBuilder> wrap = stringBuilderPool.GetInstance())
                {
                    StringBuilder sb = wrap.Instance;
                    sb.Length.Should().Be(0);
                }
            }
        }

        [Fact]
        public void StringListPool()
        {
            using (var stringBuilderPool = new ObjectPool<List<string>>(() => new List<string>(), sb =>
            {
                sb.Clear();
                return sb;
            }))
            {
                using (PooledObjectWrapper<List<string>> wrap = stringBuilderPool.GetInstance())
                {
                    List<string> l = wrap.Instance;
                    l.Count.Should().Be(0);
                    l.Add("1234");
                }

                // make sure we get back a cleared list
                using (PooledObjectWrapper<List<string>> wrap = stringBuilderPool.GetInstance())
                {
                    List<string> l = wrap.Instance;
                    l.Count.Should().Be(0);
                }
            }
        }
    }
}
