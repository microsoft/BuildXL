// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.Monitor.App.Notifications;
using BuildXL.Cache.Monitor.App.Scheduling;
using BuildXL.Cache.Monitor.Library.Client;
using BuildXL.Cache.Monitor.Library.IcM;
using BuildXL.Cache.Monitor.Library.Notifications;
using BuildXL.Cache.Monitor.Library.Rules.Kusto;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.Monitor.App.Rules.Kusto
{
    public class RulesTest
    {
        private MockNotifier<Notification> _notifier = new MockNotifier<Notification>();
        private const MonitorEnvironment TestEnvironment = MonitorEnvironment.CloudBuildTest;

        [Fact]
        public async Task CheckpointSizeTestAsync()
        {
            (var mockKusto, var baseConfiguration, var mockIcm) = await CreateClientAndConfigAsync();
            var configuration = new CheckpointSizeRule.Configuration(baseConfiguration);
            var rule = new CheckpointSizeRule(configuration);
            var ruleContext = new RuleContext(Guid.NewGuid(), SystemClock.Instance.UtcNow, new CancellationToken());

            var detectionHorizon = SystemClock.Instance.UtcNow - configuration.AnomalyDetectionHorizon;
            var size = 1;
            mockKusto.Add(new object[] {
                new CheckpointSizeRule.Result() { Stamp = "DM_S1", PreciseTimeStamp = detectionHorizon + TimeSpan.FromMinutes(1), TotalSize = size},
                new CheckpointSizeRule.Result() { Stamp = "DM_S1", PreciseTimeStamp = detectionHorizon + TimeSpan.FromHours(1), TotalSize = size * 2},
            });

            await rule.Run(ruleContext);
            _notifier.Results.Count.Should().Be(2);
            _notifier.Results[0].Message.Should().Contain("higher than the threshold");
            _notifier.Results[1].Message.Should().Contain("out of valid range");
        }

        [Fact]
        public async Task EventHubProcessingDelayTestAsync()
        {
            (var mockKusto, var baseConfiguration, var mockIcm) = await CreateClientAndConfigAsync();
            var configuration = new EventHubProcessingDelayRule.Configuration(baseConfiguration);
            var rule = new EventHubProcessingDelayRule(configuration);
            var ruleContext = new RuleContext(Guid.NewGuid(), SystemClock.Instance.UtcNow, new CancellationToken());

            mockKusto.Add(new object[] {
                new EventHubProcessingDelayRule.Result() { Stamp = "DM_S0", MaxDelay = configuration.Thresholds.Warning!.Value},
            });

            mockKusto.Add(new object[]
            {
                new EventHubProcessingDelayRule.Result2() { OutstandingBatches = 1, ProcessedBatches = 0}
            });

            await rule.Run(ruleContext);
            _notifier.Results.Count.Should().Be(2);
        }

        [Fact]
        public async Task LastProducedCheckpointTestAsync()
        {
            (var mockKusto, var baseConfiguration, var mockIcm) = await CreateClientAndConfigAsync();
            var configuration = new LastProducedCheckpointRule.Configuration(baseConfiguration);
            var rule = new LastProducedCheckpointRule(configuration);
            var ruleContext = new RuleContext(Guid.NewGuid(), SystemClock.Instance.UtcNow, new CancellationToken());

            mockKusto.Add(new object[] {
                new LastProducedCheckpointRule.Result() { Stamp = "DM_S0", Age = configuration.AgeThresholds.Warning!.Value }
            });

            await rule.Run(ruleContext);
            _notifier.Results.Count.Should().Be(2);
        }

        [Fact]
        public async Task LastRestoredCheckpointTestAsync()
        {
            (var mockKusto, var baseConfiguration, var mockIcm) = await CreateClientAndConfigAsync(4);
            var configuration = new LastRestoredCheckpointRule.Configuration(baseConfiguration);
            var rule = new LastRestoredCheckpointRule(configuration);
            var ruleContext = new RuleContext(Guid.NewGuid(), SystemClock.Instance.UtcNow, new CancellationToken());

            mockKusto.Add(new object[] {
                new LastRestoredCheckpointRule.Result() { Stamp = "DM_S0",  },
                new LastRestoredCheckpointRule.Result() { Stamp = "DM_S2", Age = configuration.CheckpointAgeErrorThreshold, LastRestoreTime = DateTime.Now },
                new LastRestoredCheckpointRule.Result() { Stamp = "DM_S2", Age = configuration.CheckpointAgeErrorThreshold, LastRestoreTime = DateTime.Now },
                new LastRestoredCheckpointRule.Result() { Stamp = "DM_S2", Age = configuration.CheckpointAgeErrorThreshold, LastRestoreTime = DateTime.Now },
                new LastRestoredCheckpointRule.Result() { Stamp = "DM_S2", Age = configuration.CheckpointAgeErrorThreshold, LastRestoreTime = DateTime.Now },
                new LastRestoredCheckpointRule.Result() { Stamp = "DM_S2", Age = configuration.CheckpointAgeErrorThreshold, LastRestoreTime = DateTime.Now },
                new LastRestoredCheckpointRule.Result() { Stamp = "DM_S3", Age = configuration.CheckpointAgeErrorThreshold - TimeSpan.FromSeconds(1), LastRestoreTime = DateTime.Now },
            });

            await rule.Run(ruleContext);
            _notifier.Results.Count.Should().Be(2);
            _notifier.Results[0].Severity.Should().Be(Severity.Info);
            _notifier.Results[1].Severity.Should().Be(Severity.Fatal);
        }

        [Fact]
        public async Task LongCopyRuleTestAsync()
        {
            (var mockKusto, var baseConfiguration, var mockIcm) = await CreateClientAndConfigAsync(4);
            var configuration = new LongCopyRule.Configuration(baseConfiguration);
            var rule = new LongCopyRule(configuration);
            var ruleContext = new RuleContext(Guid.NewGuid(), SystemClock.Instance.UtcNow, new CancellationToken());
            var result = new LongCopyRule.Result() { Stamp = "DM_S0", LongCopies = 1, TotalCopies = 10 };
            mockKusto.Add(new object[] { result });

            await rule.Run(ruleContext);
            _notifier.Results.Count.Should().Be(2);
            if ((result.LongCopies / result.TotalCopies * 100) >= configuration.LongCopiesPercentThresholds.Fatal)
            {
                _notifier.Results[0].Severity.Should().Be(Severity.Fatal);
            }
            _notifier.Results[1].Severity.Should().Be(Severity.Info);
        }

        [Fact]
        public async Task OperationFailureCheckTestAsync()
        {
            (var mockKusto, var baseConfiguration, var mockIcm) = await CreateClientAndConfigAsync();
            var check = new OperationFailureCheckRule.Check();
            check.Name = "TEST_CHECK";
            var configuration = new OperationFailureCheckRule.Configuration(baseConfiguration, check);
            var rule = new OperationFailureCheckRule(configuration);
            var ruleContext = new RuleContext(Guid.NewGuid(), SystemClock.Instance.UtcNow, new CancellationToken());

            mockKusto.Add(new object[] {
                new OperationFailureCheckRule.Result() { Stamp = "DM_S0", Machines = 0, Count = 0},
                new OperationFailureCheckRule.Result() { Stamp = "DM_S1", Machines = check.MachinesThresholds.Info!.Value + 1, Count = check.CountThresholds.Info!.Value + 1},
            });

            await rule.Run(ruleContext);
            _notifier.Results.Count.Should().Be(1);
        }

        [Fact]
        public async Task OperationPerformanceOutliersTestAsync()
        {
            var clock = SystemClock.Instance;
            (var mockKusto, var baseConfiguration, var mockIcm) = await CreateClientAndConfigAsync();
            var check = new OperationPerformanceOutliersRule.DynamicCheck();
            check.Name = "TEST_CHECK";
            var configuration = new OperationPerformanceOutliersRule.Configuration(baseConfiguration, check);
            var rule = new OperationPerformanceOutliersRule(configuration);
            var ruleContext = new RuleContext(Guid.NewGuid(), SystemClock.Instance.UtcNow, new CancellationToken());

            var results = new object[configuration.Check.FailureThresholds.Warning!.Value + 1];
            int i;
            for (i = 0; i < configuration.Check.FailureThresholds.Warning!.Value; i++)
            {
                results[i] = new OperationPerformanceOutliersRule.Result() { Stamp = "DM_S0", PreciseTimeStamp = clock.UtcNow + TimeSpan.FromSeconds(i), Machine = "Machine_0" };
            }

            results[i] = new OperationPerformanceOutliersRule.Result() { Stamp = "DM_S1", PreciseTimeStamp = clock.UtcNow, Machine = "Machine_1" };
            mockKusto.Add(results);

            await rule.Run(ruleContext);
            _notifier.Results.Count.Should().Be(2);
        }

        [Fact]
        public async Task ServiceRestartsTestAsync()
        {
            (var mockKusto, var baseConfiguration, var mockIcm) = await CreateClientAndConfigAsync();
            var configuration = new ServiceRestartsRule.Configuration(baseConfiguration);
            var rule = new ServiceRestartsRule(configuration);
            var ruleContext = new RuleContext(Guid.NewGuid(), SystemClock.Instance.UtcNow, new CancellationToken());

            var results = new object[configuration.MachinesThresholds.Warning!.Value + 1];
            var exceptionType = "TEST_EXCEPTION";
            int i;
            for (i = 0; i < configuration.MachinesThresholds.Warning!.Value; i++)
            {
                results[i] = new ServiceRestartsRule.Result() { Stamp = "DM_S0", ExceptionType = exceptionType, Machine = $"Machine_{i}" };
            }

            results[i] = new ServiceRestartsRule.Result() { Stamp = "DM_S1", ExceptionType = exceptionType, Machine = $"Machine_{i}" };
            mockKusto.Add(results);

            await rule.Run(ruleContext);
            _notifier.Results.Count.Should().Be(2);
            _notifier.Results[0].Severity.Should().Be(Severity.Warning);
        }

        private async Task<(MockKustoClient, MultiStampRuleConfiguration, MockIcmClient)> CreateClientAndConfigAsync(int numStamps = 2)
        {
            var ring = "TEST_RING";

            var mockKusto = new MockKustoClient();
            var stamps = new object[numStamps];
            for (var i = 0; i < numStamps; i++)
            {
                stamps[i] = new Watchlist.DynamicStampProperties() { Stamp = $"DM_S{i}", Ring = ring };
            }

            mockKusto.Add(stamps);
            var watchlist = await GetWatchListAsync(mockKusto);
            _notifier = new MockNotifier<Notification>();

            var mockIcm = new MockIcmClient();
            var baseConfiguration = new MultiStampRuleConfiguration(
                SystemClock.Instance,
                TestGlobal.Logger,
                _notifier,
                mockKusto,
                mockIcm,
                Constants.DefaultEnvironments[TestEnvironment].KustoDatabaseName,
                TestEnvironment,
                watchlist);

            return (mockKusto, baseConfiguration, mockIcm);
        }

        private Task<Watchlist> GetWatchListAsync(MockKustoClient mockKusto)
        {
            var environments = new Dictionary<MonitorEnvironment, EnvironmentConfiguration>() {
                {
                    TestEnvironment, Constants.DefaultEnvironments[TestEnvironment]
                }
            };

            var resources = new Dictionary<MonitorEnvironment, IKustoClient>()
            {
                {
                    TestEnvironment, mockKusto
                }
            };

            return Watchlist.CreateAsync(TestGlobal.Logger, environments, resources);
        }
    }
}
