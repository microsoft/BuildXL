// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Stores
{
    public abstract class QuotaRuleTests : TestBase
    {
        protected QuotaRuleTests(Func<IAbsFileSystem> createFileSystemFunc, ILogger logger)
            : base(createFileSystemFunc, logger)
        {
        }

        protected QuotaRuleTests()
            : base(TestGlobal.Logger)
        {
        }

        private static void AssertNoPurging(PurgeResult result)
        {
            result.EvictedFiles.Should().Be(0);
            result.EvictedSize.Should().Be(0);
            result.PinnedSize.Should().Be(0);
        }

        private static void AssertPurged(PurgeResult result, EvictResult expectedResult)
        {
            result.EvictedFiles.Should().Be(expectedResult.EvictedFiles);
            result.EvictedSize.Should().Be(expectedResult.EvictedSize);
            result.PinnedSize.Should().Be(expectedResult.PinnedSize);
        }

        protected abstract IQuotaRule CreateRule(long currentSize, EvictResult evictResult = null);

        protected abstract long SizeWithinTargetQuota { get; }

        protected abstract long SizeBeyondTargetQuota { get; }
    }
}
