// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.Tracing;
using BuildXL.Cache.Compositing;

namespace BuildXL.Cache.Tests
{
    public class TestCompositing : TestCacheBackingstore
    {
        private static readonly string compositingCacheConfigJSONData = @"{{
            ""Assembly"":""BuildXL.Cache.Compositing"",
            ""Type"":""BuildXL.Cache.Compositing.CompositingCacheFactory"",
            ""CacheId"":""{0}"",
            ""StrictMetadataCasCoupling"":{1},
            ""MetadataCache"":{2},
            ""CasCache"":{3}
        }}";

        public override string NewCache(string cacheId, bool strictMetadataCasCoupling, bool authoritative = false)
        {
            TestInMemory memory = new TestInMemory();

            return string.Format(compositingCacheConfigJSONData, cacheId, strictMetadataCasCoupling.ToString().ToLower(), memory.NewCache(cacheId + "Metadata", false), memory.NewCache(cacheId + "CAS", false));
        }

        protected override IEnumerable<EventSource> EventSources => new EventSource[] { CompositingCache.EventSource };
    }
}
