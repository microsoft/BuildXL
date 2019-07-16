// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;
using BuildXL.Cache.InMemory;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace BuildXL.Cache.Tests
{
    public class TestInMemory : TestCacheBackingstore
    {
        private static readonly string inMemoryCacheConfigJSONData = @"{{ 
            ""Assembly"":""BuildXL.Cache.InMemory"",
            ""Type"":""BuildXL.Cache.InMemory.MemCacheFactory"", 
            ""CacheId"":""{0}"",
            ""StrictMetadataCasCoupling"":{1},
            ""IsAuthoritative"":{2}
        }}";

        public override string NewCache(string cacheId, bool strictMetadataCasCoupling, bool authoritative = false)
        {
            return string.Format(inMemoryCacheConfigJSONData, cacheId, strictMetadataCasCoupling.ToString().ToLower(), authoritative.ToString().ToLower());
        }

        // This is a config string that should cause the in-memory cache to fail to get constructed
        private static readonly string inMemoryCacheConfigFailureJSONData = @"{{ 
            ""Assembly"":""BadAssemblyNameShouldFail"",
            ""Type"":""BuildXL.Cache.InMemory.MemCacheFactory"", 
            ""CacheId"":""{0}"",
            ""StrictMetadataCasCoupling"":{1},
            ""IsAuthoritative"":{2}
        }}";

        public string NewCacheFailure(string cacheId, bool strictMetadataCasCoupling, bool authoritative = false)
        {
            return string.Format(inMemoryCacheConfigFailureJSONData, cacheId, strictMetadataCasCoupling.ToString().ToLower(), authoritative.ToString().ToLower());
        }

        protected override IEnumerable<EventSource> EventSources => new EventSource[0];

        protected override bool CanTestCorruption => true;

        protected override Task CorruptCasEntry(ICache cache, CasHash hash)
        {
            XAssert.IsNotNull(cache as MemCache, "Invalid cache passed to TestInMemory CorruptCasEntry test method");
            return CorruptEntry(cache, hash);
        }

        // For use by the aggregator test for corruption
        public static Task CorruptEntry(ICache cache, CasHash hash)
        {
            // We use this as a Task.Run() just to help prove the test structure
            // Other caches are likely to need async behavior so we needed to support
            // that.  No return result.  This must fail if it can not work.
            return Task.Run(() =>
            {
                MemCache mem = cache as MemCache;
                XAssert.IsNotNull(mem, "Invalid cache passed to TestInMemory.CorruptEntry test method");

                byte[] bytesToCorrupt;
                XAssert.IsTrue(mem.CasStorage.TryGetValue(hash, out bytesToCorrupt), "Failed to get hash to corrupt!");
                XAssert.AreNotEqual(0, bytesToCorrupt.Length, "Can not corrupt the zero-length entry");

                // Just flip some bits
                for (int i = 0; i < bytesToCorrupt.Length; i++)
                {
                    bytesToCorrupt[i] ^= 3;
                }
            });
        }

        /// <nodoc/>
        [Fact]
        public async Task FailToCreateCache()
        {
            const string TestName = "FailToCreateCache";
            string testCacheId = MakeCacheId(TestName);

            string cacheConfigData = NewCacheFailure(testCacheId, true);
            Possible<ICache, Failure> cachePossible = await InitializeCacheAsync(cacheConfigData);

            XAssert.IsFalse(cachePossible.Succeeded, "This should have failed cache construction");
        }
    }
}
