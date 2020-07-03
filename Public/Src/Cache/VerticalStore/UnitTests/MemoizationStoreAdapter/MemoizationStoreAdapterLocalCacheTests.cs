// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.Tests
{
    public class MemoizationStoreAdapterLocalCacheTests : TestMemoizationStoreAdapterLocalCacheBase
    {
        protected override string DefaultMemoizationStoreJsonConfigString => @"{{
            ""Assembly"":""BuildXL.Cache.MemoizationStoreAdapter"",
            ""Type"":""BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory"",
            ""CacheId"":""{0}"",
            ""MaxCacheSizeInMB"":{1},
            ""MaxStrongFingerprints"":{2},
            ""CacheRootPath"":""{3}"",
            ""CacheLogPath"":""{4}"",
            ""SingleInstanceTimeoutInSeconds"":10,
            ""ApplyDenyWriteAttributesOnContent"":""true""
        }}";
    }
}
