// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStoreAdapter;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace BuildXL.Cache.Tests
{
    public sealed class MemoizationStoreAdapterWithStreamCasLocalCacheTests : TestMemoizationStoreAdapterLocalCacheBase
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
            ""ApplyDenyWriteAttributesOnContent"":""true"",
            ""UseStreamCAS"":""true""}}";

        [Fact]
        public async Task TestDefaultForStreamCas()
        {     
            var jsonConfig = NewCache(nameof(TestDefaultForStreamCas), true);
            ICacheConfigData cacheData;
            Exception exception;
            var success = CacheFactory.TryCreateCacheConfigData(jsonConfig, out cacheData, out exception);

            XAssert.IsTrue(success);
            XAssert.IsNull(exception);

            var maybeCacheConfig = cacheData.Create<MemoizationStoreCacheFactory.Config>();
            XAssert.IsTrue(maybeCacheConfig.Succeeded);

            var cacheConfig = maybeCacheConfig.Result;
            XAssert.IsTrue(cacheConfig.UseStreamCAS);

            var rootForStream = Path.Combine(cacheConfig.CacheRootPath, "streams");

            Possible<ICache, Failure> cachePossible = await InitializeCacheAsync(jsonConfig);
            ICache cache = cachePossible.Success();
            XAssert.IsTrue(Directory.Exists(rootForStream));

            await ShutdownCacheAsync(cache, nameof(TestDefaultForStreamCas));
        }
    }
}
