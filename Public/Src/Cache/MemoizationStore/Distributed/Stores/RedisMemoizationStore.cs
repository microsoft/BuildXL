// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Logging;
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
