// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.Tracing;

namespace BuildXL.Cache.Tests
{
    /// <summary>
    /// Tests the callback cache wrapper to ensure it works correctly when no callbacks are registered.
    /// </summary>
    public class TestCallbackCache : TestCacheBackingstore
    {
        private static readonly string callbackCacheConfigJSONData = @"{{ 
            ""Assembly"":""BuildXL.Cache.Interfaces.Test"",
            ""Type"":""BuildXL.Cache.Interfaces.Test.CallbackCacheFactory"", 
            ""EncapsulatedCache"":{0}
        }}";

        public override string NewCache(string cacheId, bool strictMetadataCasCoupling, bool authoritative = false)
        {
            TestInMemory memoryCache = new TestInMemory();

            string memoryString = memoryCache.NewCache(cacheId, strictMetadataCasCoupling);
            return FormatNewCacheConfig(memoryString);
        }

        public static string FormatNewCacheConfig(string wrappedCacheConfig)
        {
            return string.Format(callbackCacheConfigJSONData, wrappedCacheConfig);
        }

        protected override IEnumerable<EventSource> EventSources => new EventSource[0];
    }
}
