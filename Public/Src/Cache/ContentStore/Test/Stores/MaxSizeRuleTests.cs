// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Stores
{
    public class MaxSizeRuleTests : QuotaRuleTests
    {
        private const long Hard = 100;
        private const long Soft = 90;

        protected override long SizeWithinTargetQuota => Soft - 5;

        protected override long SizeBeyondTargetQuota => Soft + 5;

        [Theory]
        [InlineData(101, 0, false)]
        [InlineData(101, 20, false)]
        [InlineData(100, 0, true)]
        [InlineData(100, 1, false)]
        [InlineData(100, 20, false)]
        [InlineData(99, 1, true)]
        [InlineData(99, 2, false)]
        [InlineData(99, 20, false)]
        public void IsInsideHardLimitResult(long currentSize, long reserveSize, bool result)
        {
            CreateRule(currentSize).IsInsideHardLimit(reserveSize).Succeeded.Should().Be(result);
        }

        [Theory]
        [InlineData(91, 0, false)]
        [InlineData(91, 20, false)]
        [InlineData(90, 0, true)]
        [InlineData(90, 1, false)]
        [InlineData(90, 20, false)]
        [InlineData(88, 2, true)]
        [InlineData(88, 5, false)]
        [InlineData(88, 20, false)]
        public void IsInsideSoftLimitResult(long currentSize, long reserveSize, bool result)
        {
            CreateRule(currentSize).IsInsideSoftLimit(reserveSize).Succeeded.Should().Be(result);
        }

        [Theory]
        [InlineData(90, 0, false)]
        [InlineData(90, 20, false)]
        [InlineData(89, 0, true)]
        [InlineData(89, 1, false)]
        [InlineData(89, 20, false)]
        [InlineData(88, 1, true)]
        [InlineData(88, 2, false)]
        [InlineData(88, 20, false)]
        public void IsInsideTargetLimitResult(long currentSize, long reserveSize, bool result)
        {
            CreateRule(currentSize).IsInsideTargetLimit(reserveSize).Succeeded.Should().Be(result);
        }

        protected override IQuotaRule CreateRule(long currentSize, EvictResult evictResult = null)
        {
            return new MaxSizeRule(
                new MaxSizeQuota(Hard, Soft),
                (context, contentHashInfo, onlyUnlinked) => Task.FromResult(evictResult ?? new EvictResult("error")),
                () => currentSize);
        }
    }
}
