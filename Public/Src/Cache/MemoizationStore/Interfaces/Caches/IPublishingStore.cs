// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Caches
{
    /// <nodoc />
    public interface IPublishingStore : IStartupShutdownSlim
    {
        /// <nodoc />
        Result<IPublishingSession> CreateSession(Context context, string name, PublishingCacheConfiguration config, string pat);
    }
}
