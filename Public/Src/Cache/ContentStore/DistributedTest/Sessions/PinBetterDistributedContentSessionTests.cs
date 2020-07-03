// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using ContentStoreTest.Distributed.Redis;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Sessions
{
    [Collection("Redis-based tests")]
    [Trait("Category", "LongRunningTest")]
    public class PinBetterDistributedContentSessionTests : DistributedContentSessionTests
    {
        public PinBetterDistributedContentSessionTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(redis, output)
        {
        }

        protected override DistributedContentStoreSettings CreateSettings()
        {
            var settings = base.CreateSettings();
            settings.PinConfiguration = new PinConfiguration();
            return settings;
        }
    }
}
