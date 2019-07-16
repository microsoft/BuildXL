// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.MemoizationStore.Tracing;

namespace BuildXL.Cache.MemoizationStore.Stores
{
    internal class RocksDbMemoizationStoreTracer : MemoizationStoreTracer
    {
        public RocksDbMemoizationStoreTracer(ILogger logger, string name)
            : base(logger, name)
        {
        }
    }
}
