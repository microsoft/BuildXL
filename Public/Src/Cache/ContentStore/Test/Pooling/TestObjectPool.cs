// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Pooling;

namespace ContentStoreTest.Pooling
{
    internal class TestObjectPool<T> : ObjectPool<T>
        where T : class
    {
        public TestObjectPool(Func<T> creator, Func<T, T> cleanup)
            : base(creator, cleanup)
        {
        }

        public void PutInstanceForTest(PooledObjectWrapper<T> wrapper)
        {
            PutInstance(wrapper);
        }
    }
}
