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

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public async Task NoPurgingIf(bool withinTargetQuota, bool canceled)
        {
            var evictResult = new EvictResult(10, 20, 30, lastAccessTime: DateTime.Now, effectiveLastAccessTime: null, successfullyEvictedHash: true, replicaCount: 1);
            var rule = CreateRule(withinTargetQuota ? SizeWithinTargetQuota : SizeBeyondTargetQuota, evictResult);

            using (var cts = new CancellationTokenSource())
            {
                if (canceled)
                {
                    cts.Cancel();
                }

                var result = await rule.PurgeAsync(new Context(Logger), 1, new[] { new ContentHashWithLastAccessTimeAndReplicaCount(ContentHash.Random(), DateTime.UtcNow) }, cts.Token);

                // No purging if within quota, there's an active sensitive session, or cancellation has been requested.
                if (withinTargetQuota || canceled)
                {
                    AssertNoPurging(result);
                }
                else
                {
                    AssertPurged(result, evictResult);
                }
            }
        }

        [Fact]
        public async Task ErrorPropagated()
        {
            var evictResult = new EvictResult($"{nameof(ErrorPropagated)} test error.");
            var rule = CreateRule(SizeBeyondTargetQuota, evictResult);

            using (var cts = new CancellationTokenSource())
            {
                var result = await rule.PurgeAsync(new Context(Logger), 10, new[] { new ContentHashWithLastAccessTimeAndReplicaCount(ContentHash.Random(), DateTime.UtcNow) }, cts.Token);
                result.ShouldBeError(evictResult.ErrorMessage);
            }
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
