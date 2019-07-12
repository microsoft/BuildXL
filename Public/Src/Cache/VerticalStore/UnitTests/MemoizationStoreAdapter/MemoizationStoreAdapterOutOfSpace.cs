// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStoreAdapter;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace BuildXL.Cache.Tests
{
    public sealed class MemoizationStoreAdapterOutOfSpace : TestCacheCore
    {
        private const string MemoizationStoreJsonConfigString = @"{{
            ""Assembly"":""BuildXL.Cache.MemoizationStoreAdapter"",
            ""Type"":""BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory"",
            ""CacheId"":""{0}"",
            ""MaxCacheSizeInMB"":{1},
            ""MaxStrongFingerprints"":{2},
            ""CacheRootPath"":""{3}"",
            ""CacheLogPath"":""{4}"",
            ""SingleInstanceTimeoutInSeconds"":10,
            ""ApplyDenyWriteAttributesOnContent"":""true"",
            ""UseStreamCAS"":""false""}}";

        protected override IEnumerable<EventSource> EventSources => new EventSource[0];

        public override string NewCache(string cacheId, bool strictMetadataCasCoupling, bool authoritative = false)
        {
            var cacheDir = ConvertToJSONCompatibleString(GenerateCacheFolderPath("MemoStore."));
            var cacheLogDir = ConvertToJSONCompatibleString(Path.Combine(cacheDir, "Cache.Log"));
            var jsonConfigString = string.Format(MemoizationStoreJsonConfigString, cacheId, 1, 10, cacheDir, cacheLogDir);

            return jsonConfigString;
        }

        private async Task TestForOutOfSpace(string cacheId, Func<ICacheSession, Task> testSessionAsyncFunc)
        {
            var jsonConfig = NewCache(cacheId, true);
            ICacheConfigData cacheData;
            Exception exception;
            var success = CacheFactory.TryCreateCacheConfigData(jsonConfig, out cacheData, out exception);

            XAssert.IsTrue(success);
            XAssert.IsNull(exception);

            var maybeCacheConfig = cacheData.Create<MemoizationStoreCacheFactory.Config>();
            XAssert.IsTrue(maybeCacheConfig.Succeeded);

            Possible<ICache, Failure> cachePossible = await InitializeCacheAsync(jsonConfig);
            ICache cache = cachePossible.Success();

            string testSessionId = "Session1-" + cacheId;

            ICacheSession session = await CreateSessionAsync(cache, testSessionId);

            await testSessionAsyncFunc(session);

            await CloseSessionAsync(session, testSessionId);
            await ShutdownCacheAsync(cache, cacheId);
        }

        [Fact]
        public async Task TestSessionForOutOfSpaceAtOnce()
        {
           await TestForOutOfSpace(
                nameof(TestSessionForOutOfSpaceAtOnce),
                async session =>
                     {
                         var random = new Random();

                         // Add content exceeding quota.
                         var content = new byte[(1024 * 1024) + 1024];
                         random.NextBytes(content);

                         using (var stream = new MemoryStream(content))
                         {
                             var result = await session.AddToCasAsync(stream);
                             Assert.False(result.Succeeded);

                             string dummy;
                             Assert.True(
                                 MemoizationStoreAdapterCacheCacheSession.IsOutOfSpaceError(
                                     result.Failure.DescribeIncludingInnerFailures(),
                                     out dummy));
                         }

                         // Add content smaller than quota.
                         content = new byte[1024];
                         random.NextBytes(content);

                         using (var stream = new MemoryStream(content))
                         {
                             var result = await session.AddToCasAsync(stream);
                             Assert.False(result.Succeeded);

                             string dummy;
                             Assert.True(
                                 MemoizationStoreAdapterCacheCacheSession.IsOutOfSpaceError(
                                     result.Failure.DescribeIncludingInnerFailures(),
                                     out dummy));
                         }
                     });
        }

        [Fact]
        public async Task TestSessionForOutOfSpace()
        {
            await TestForOutOfSpace(
               nameof(TestSessionForOutOfSpaceAtOnce),
               async session =>
               {
                   var random = new Random();

                   var content = new byte[1024 + 1024];
                   random.NextBytes(content);

                   using (var stream = new MemoryStream(content))
                   {
                       var result = await session.AddToCasAsync(stream);
                       Assert.True(result.Succeeded);
                   }

                   // Add content exceeding quota.
                   content = new byte[(1024 * 1024) - 1024];
                   random.NextBytes(content);

                   using (var stream = new MemoryStream(content))
                   {
                       var result = await session.AddToCasAsync(stream);
                       Assert.False(result.Succeeded);

                       string dummy;
                       Assert.True(
                           MemoizationStoreAdapterCacheCacheSession.IsOutOfSpaceError(
                               result.Failure.DescribeIncludingInnerFailures(),
                               out dummy));
                   }

                   // Add content smaller than quota.
                   content = new byte[1024];
                   random.NextBytes(content);

                   using (var stream = new MemoryStream(content))
                   {
                       var result = await session.AddToCasAsync(stream);
                       Assert.False(result.Succeeded);

                       string dummy;
                       Assert.True(
                           MemoizationStoreAdapterCacheCacheSession.IsOutOfSpaceError(
                               result.Failure.DescribeIncludingInnerFailures(),
                               out dummy));
                   }
               });
        }
    }
}
