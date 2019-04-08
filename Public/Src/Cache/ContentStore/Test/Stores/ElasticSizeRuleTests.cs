// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Stores
{
    public class ElasticSizeRuleTests : QuotaRuleTests
    {
        private const long Hard = 100;

        private const long Soft = 90;

        protected override long SizeWithinTargetQuota => Soft - 5;

        protected override long SizeBeyondTargetQuota => Soft + 5;

        private readonly ITestClock _clock = new TestSystemClock();

        public ElasticSizeRuleTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        [Theory]
        [InlineData(101, 0, true)]
        [InlineData(101, 1, true)]
        [InlineData(101, 20, false)]
        [InlineData(101, 30, false)]
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

        [Theory]
        [InlineData(50, Hard, null, null, Hard, new long[0], ElasticSizeRule.DefaultHistoryWindowSize, ElasticSizeRule.DefaultCalibrationCoefficient, false, null)]
        [InlineData(50, Hard, null, null, Soft + 5, new long[0], ElasticSizeRule.DefaultHistoryWindowSize, ElasticSizeRule.DefaultCalibrationCoefficient, false, null)]
        [InlineData(50, Hard, null, null, Soft, new long[0], ElasticSizeRule.DefaultHistoryWindowSize, ElasticSizeRule.DefaultCalibrationCoefficient, false, null)]
        [InlineData(50, Hard, null, null, Soft - 5, new long[0], ElasticSizeRule.DefaultHistoryWindowSize, ElasticSizeRule.DefaultCalibrationCoefficient, false, null)]
        [InlineData(50, Hard, null, null, 110, new long[0], ElasticSizeRule.DefaultHistoryWindowSize, ElasticSizeRule.DefaultCalibrationCoefficient, true, (long)(110 * ElasticSizeRule.DefaultCalibrationCoefficient))]
        [InlineData(50, Hard, 120L, new long[0], 95, new long[] {70, 60, 75}, 3, ElasticSizeRule.DefaultCalibrationCoefficient, true, Hard)]
        [InlineData(50, Hard, null, null, 95, new long[] {70, 80, 75}, 3, ElasticSizeRule.DefaultCalibrationCoefficient, false, Hard)]
        [InlineData(50, Hard, null, null, 95, new long[] {70, 75}, 3, ElasticSizeRule.DefaultCalibrationCoefficient, false, Hard)]
        [InlineData(50, Hard, 120L, new long[0], Hard, new long[] {70, 60, 75}, 3, ElasticSizeRule.DefaultCalibrationCoefficient, true, (long)(Hard / ElasticSizeRule.CurrentSizeHardLimitRatio))]
        [InlineData(50, Hard, 120L, new long[0], Hard, new long[] {70, 60}, 3, ElasticSizeRule.DefaultCalibrationCoefficient, true, (long)(Hard / ElasticSizeRule.CurrentSizeHardLimitRatio))]
        [InlineData(50, Hard, 120L, new long[0], 120, new long[] {20, 10, 25}, 3, ElasticSizeRule.DefaultCalibrationCoefficient, true, (long)(120 / ElasticSizeRule.CurrentSizeHardLimitRatio))]
        [InlineData(990, 500, null, null, 991, new long[0], 3, ElasticSizeRule.DefaultCalibrationCoefficient, false, null)]
        [InlineData(990, 500, null, null, 991, new long[] {20, 10, 25}, 3, ElasticSizeRule.DefaultCalibrationCoefficient, false, null)]
        [InlineData(990, 500, null, null, 1000, new long[0], ElasticSizeRule.DefaultHistoryWindowSize, ElasticSizeRule.DefaultCalibrationCoefficient, false, null)]
        [InlineData(990, 500, null, null, 1001, new long[0], ElasticSizeRule.DefaultHistoryWindowSize, ElasticSizeRule.DefaultCalibrationCoefficient, true, (long)(1001 * ElasticSizeRule.DefaultCalibrationCoefficient))]
        public async Task TestCalibration(
            long initialSize,
            long initialHardLimit,
            long? intermediateNewSize,
            long[] intermediateNewHistory,
            long finalNewSize,
            long[] finalNewHistory,
            int windowSize,
            double calibrateCoefficient,
            bool shouldBeCalibrated,
            long? newHardLimit)
        {
            using (var root = new DisposableDirectory(FileSystem))
            {
                long size = initialSize;
                PinSizeHistory pinSizeHistory = await PinSizeHistory.LoadOrCreateNewAsync(FileSystem, _clock, root.Path);
                Func<long> currentSizeFunc = () => Volatile.Read(ref size);
                Func<int, PinSizeHistory.ReadHistoryResult> readHistoryFunc = ws => pinSizeHistory.ReadHistory(ws);

                var elasticRule = CreateElasticRule(root.Path, initialHardLimit, currentSizeFunc, readHistoryFunc, windowSize, calibrateCoefficient);
                elasticRule.CanBeCalibrated.Should().BeTrue("Quota should be able to be calibrated");

                if (initialHardLimit >= initialSize)
                {
                    elasticRule.CurrentQuota.Hard.Should().Be(initialHardLimit, "Hard quota should be equal to as initial");
                }
                else
                {
                    elasticRule.CurrentQuota.Hard.Should().Be((long)(initialSize / ElasticSizeRule.CurrentSizeHardLimitRatio), "Hard quota should be around the initial");
                }

                if (intermediateNewSize.HasValue && intermediateNewHistory != null)
                {
                    Volatile.Write(ref size, intermediateNewSize.Value);

                    await elasticRule.CalibrateAsync().ShouldBeSuccess();
                    _clock.Increment();
                }

                Volatile.Write(ref size, finalNewSize);

                foreach (var h in finalNewHistory)
                {
                    pinSizeHistory.Add(h);
                    _clock.Increment();
                }

                var result = await elasticRule.CalibrateAsync();
                result.Succeeded.Should().Be(shouldBeCalibrated, $"this error [{result.ErrorMessage}] should not happen");

                if (newHardLimit.HasValue)
                {
                    elasticRule.CurrentQuota.Hard.Should().Be(newHardLimit.Value);
                }
            }
        }

        protected override IQuotaRule CreateRule(long currentSize, EvictResult evictResult = null)
        {
            var root = new DisposableDirectory(FileSystem);
            Func<IQuotaRule> ruleFactory = () =>
            {
                var initialQuota = new MaxSizeQuota(Hard);
                return new ElasticSizeRule(
                    ElasticSizeRule.DefaultHistoryWindowSize,
                    initialQuota,
                    (context, contentHashInfo, onlyUnlinked) => Task.FromResult(evictResult ?? new EvictResult("error")),
                    () => currentSize,
                    windowSize => new PinSizeHistory.ReadHistoryResult(new long[0], DateTime.UtcNow.Ticks),
                    FileSystem,
                    root.Path);
            };

            return new ForwardingQuotaRule(ruleFactory, root);
        }

        private ElasticSizeRule CreateElasticRule(
            AbsolutePath root,
            long initialHardLimit,
            Func<long> currentSizeFunc,
            Func<int, PinSizeHistory.ReadHistoryResult> readHistoryFunc,
            int windowSize = ElasticSizeRule.DefaultHistoryWindowSize,
            double calibrateCoefficient = ElasticSizeRule.DefaultCalibrationCoefficient)
        {
            var initialQuota = new MaxSizeQuota(initialHardLimit);
            return new ElasticSizeRule(
                windowSize,
                initialQuota,
                (context, contentHashInfo, onlyUnlinked) => Task.FromResult(new EvictResult("error")),
                currentSizeFunc,
                readHistoryFunc,
                FileSystem,
                root,
                calibrateCoefficient);
        }

        private class ForwardingQuotaRule : IDisposable, IQuotaRule
        {
            public readonly IQuotaRule Rule;
            public readonly DisposableDirectory Root;
            private bool _freed;

            public ForwardingQuotaRule(Func<IQuotaRule> ruleFactory, DisposableDirectory root)
            {
                Rule = ruleFactory();
                Root = root;
                _freed = false;
            }

            /// <inheritdoc />
            public bool OnlyUnlinked => Rule.OnlyUnlinked;

            /// <inheritdoc />
            public ContentStoreQuota Quota => Rule.Quota;

            public void Dispose()
            {
                Free();
            }

            public BoolResult IsInsideHardLimit(long reserveSize = 0)
            {
                return Rule.IsInsideHardLimit(reserveSize);
            }

            public BoolResult IsInsideSoftLimit(long reserveSize = 0)
            {
                return Rule.IsInsideSoftLimit(reserveSize);
            }

            public BoolResult IsInsideTargetLimit(long reserveSize = 0)
            {
                return Rule.IsInsideTargetLimit(reserveSize);
            }

            public Task<PurgeResult> PurgeAsync(Context context, long reserveSize, IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithInfo, CancellationToken token)
            {
                return Rule.PurgeAsync(context, reserveSize, contentHashesWithInfo, token);
            }

            public bool CanBeCalibrated => Rule.CanBeCalibrated;

            public Task<CalibrateResult> CalibrateAsync()
            {
                return Rule.CalibrateAsync();
            }

            public bool IsEnabled
            {
                get => Rule.IsEnabled;
                set => Rule.IsEnabled = value;
            }

            private void Free()
            {
                if (_freed)
                {
                    return;
                }

                _freed = true;
                Root.Dispose();
            }
        }
    }
}
