// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Stores;

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    internal class RedisMemoizationStore : DatabaseMemoizationStore
    {
        /// <nodoc />
        public RedisMemoizationStore(ILogger logger, RedisMemoizationDatabase database)
            : base(database)
        {
        }
    }
}
