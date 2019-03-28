// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStoreAdapter;
using BuildXL.Cache.Tests;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.Cache.MemoizationStoreAdapter
{
    public sealed class MemoizationStoreAdapterWithNonDefaultStreamCasLocalCacheTests : TestMemoizationStoreAdapterLocalCacheBase
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
            ""UseStreamCAS"":""true"",
            ""StreamCAS"":{{
                ""MaxCacheSizeInMB"":{5},
                ""CacheRootPath"":""{6}"",
                ""SingleInstanceTimeoutInSeconds"":10,
                ""ApplyDenyWriteAttributesOnContent"":""true""
            }}}}";

        protected override string CreateJsonConfigString(string cacheId)
        {
            var cacheDir = ConvertToJSONCompatibleString(GenerateCacheFolderPath("MemoStore."));
            var cacheLogDir = ConvertToJSONCompatibleString(Path.Combine(cacheDir, "Cache.Log"));
            return string.Format(DefaultMemoizationStoreJsonConfigString, cacheId, 5000, 50000, cacheDir, cacheLogDir, 5000, 
                OperatingSystemHelper.IsUnixOS? Path.Combine(cacheDir, @"streams"): cacheDir + @"\\streams");
        }

        [Fact]
        public async Task TestNonDefaultForStreamCas()
        {
            var jsonConfig = NewCache(nameof(TestNonDefaultForStreamCas), true);
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

            await ShutdownCacheAsync(cache, nameof(TestNonDefaultForStreamCas));
        }
    }
}

