// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Sessions;
using ContentStoreTest.Distributed.ContentLocation;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Cache.ContentStore.Distributed;
using Test.BuildXL.TestUtilities.Xunit;

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
