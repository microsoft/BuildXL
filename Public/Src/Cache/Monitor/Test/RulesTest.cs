// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
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
        private const CloudBuildEnvironment TestEnvironment = CloudBuildEnvironment.Test;

        [Fact]
        public async Task ActiveMachineTestAsync()
        {
            (var mockKusto, var baseConfiguration, var mockIcm) = await CreateClientAndConfigAsync(3);
            var configuration = new ActiveMachinesRule.Configuration(baseConfiguration);
            var rule = new ActiveMachinesRule(configuration);
            var ruleContext = new RuleContext(Guid.NewGuid(), SystemClock.Instance.UtcNow, new CancellationToken());

            var detectionHorizon = SystemClock.Instance.UtcNow - Constants.KustoIngestionDelay - configuration.AnomalyDetectionHorizon;

            mockKusto.Add(new object[] {
                new ActiveMachinesRule.Result() { Stamp = "DM_S1", PreciseTimeStamp = detectionHorizon - TimeSpan.FromHours(1) },
                new ActiveMachinesRule.Result() { Stamp = "DM_S2", PreciseTimeStamp = detectionHorizon - TimeSpan.FromHours(1) },
                new ActiveMachinesRule.Result() { Stamp = "DM_S2", PreciseTimeStamp = detectionHorizon + TimeSpan.FromHours(1) , ActiveMachines = 3},
            });

            await rule.Run(ruleContext);
            _notifier.Results.Count.Should().Be(3);
        }

        [Fact]
        public async Task BuildFailuresTestAsync()
        {
            (var mockKusto, var baseConfiguration, var mockIcm) = await CreateClientAndConfigAsync(3);
            var configuration = new BuildFailuresRule.Configuration(baseConfiguration);
            var rule = new BuildFailuresRule(configuration);
            var ruleContext = new RuleContext(Guid.NewGuid(), SystemClock.Instance.UtcNow, new CancellationToken());

            mockKusto.Add(new object[] {
                new BuildFailuresRule.Result() { Stamp = "DM_S1", FailureRate = 0.0},
                new BuildFailuresRule.Result() { Stamp = "DM_S2", FailureRate = configuration.FailureRateThresholds.Fatal!.Value + 0.1, Total = configuration.MinimumAmountOfBuildsForIcm },
                new BuildFailuresRule.Result() { Stamp = "DM_S3", FailureRate = configuration.FailureRateThresholds.Fatal!.Value + 0.1, Total = configuration.MinimumAmountOfBuildsForIcm - 1 },
            });

            await rule.Run(ruleContext);
            _notifier.Results.Count.Should().Be(3);
            _notifier.Results[0].Severity.Should().Be(Severity.Fatal);

            mockIcm.Incidents.Count.Should().Be(1);
        }

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
        public async Task ContractViolationsTestAsync()
        {
            (var mockKusto, var baseConfiguration, var mockIcm) = await CreateClientAndConfigAsync();
            var configuration = new ContractViolationsRule.Configuration(baseConfiguration);
            var rule = new ContractViolationsRule(configuration);
            var ruleContext = new RuleContext(Guid.NewGuid(), SystemClock.Instance.UtcNow, new CancellationToken());

            mockKusto.Add(new object[] {
                new ContractViolationsRule.Result() { Stamp = "DM_S1", Count = 1, ExceptionType = "Exception", Operation = "TEST"},
            });

            await rule.Run(ruleContext);
            _notifier.Results.Count.Should().Be(1);
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
        public async Task FireAndForgetExceptionsTestAsync()
        {
            (var mockKusto, var baseConfiguration, var mockIcm) = await CreateClientAndConfigAsync();
            var configuration = new FireAndForgetExceptionsRule.Configuration(baseConfiguration);
            var rule = new FireAndForgetExceptionsRule(configuration);
            var ruleContext = new RuleContext(Guid.NewGuid(), SystemClock.Instance.UtcNow, new CancellationToken());

            mockKusto.Add(new object[] {
                new FireAndForgetExceptionsRule.Result() { Stamp = "DM_S1", Machines = configuration.MachinesThresholds.Warning!.Value + 1, Count = configuration.MinimumErrorsThreshold - 1},
                new FireAndForgetExceptionsRule.Result() { Stamp = "DM_S1", Machines = configuration.MachinesThresholds.Warning!.Value + 1, Count = configuration.MinimumErrorsThreshold + 1},
            });

            await rule.Run(ruleContext);
            _notifier.Results.Count.Should().Be(1);
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

        [Fact]
        public async Task MachineReimagesRuleTestAsync()
        {
            (var mockKusto, var baseConfiguration, var mockIcm) = await CreateClientAndConfigAsync();
            var configuration = new MachineReimagesRule.Configuration(baseConfiguration);
            var rule = new MachineReimagesRule(configuration);
            var ruleContext = new RuleContext(Guid.NewGuid(), SystemClock.Instance.UtcNow, new CancellationToken());

            var results = new object[] {
                new MachineReimagesRule.Result() { Stamp = "S1", Service = Constants.ContentAddressableStoreService, Total = 1000, Reimaged = 2 },
                new MachineReimagesRule.Result() { Stamp = "S1", Service = Constants.ContentAddressableStoreMasterService, Total = 3, Reimaged = 0 },
                new MachineReimagesRule.Result() { Stamp = "S2", Service = Constants.ContentAddressableStoreService, Total = 1000, Reimaged = 130 },
                new MachineReimagesRule.Result() { Stamp = "S2", Service = Constants.ContentAddressableStoreMasterService, Total = 3, Reimaged = 0 },
                new MachineReimagesRule.Result() { Stamp = "S3", Service = Constants.ContentAddressableStoreService, Total = 1000, Reimaged = 240 },
                new MachineReimagesRule.Result() { Stamp = "S3", Service = Constants.ContentAddressableStoreMasterService, Total = 3, Reimaged = 0 },
                new MachineReimagesRule.Result() { Stamp = "S4", Service = Constants.ContentAddressableStoreService, Total = 1000, Reimaged = 569 },
                new MachineReimagesRule.Result() { Stamp = "S4", Service = Constants.ContentAddressableStoreMasterService, Total = 3, Reimaged = 0 },
            };

            mockKusto.Add(results);

            await rule.Run(ruleContext);

            _notifier.Results.Count.Should().Be(3);
            _notifier.Results[0].Severity.Should().Be(Severity.Warning);
            _notifier.Results[1].Severity.Should().Be(Severity.Error);
            _notifier.Results[2].Severity.Should().Be(Severity.Fatal);
        }

        [Fact]
        public async Task MachineReimagesRuleTestWithLauncherAsync()
        {
            (var mockKusto, var baseConfiguration, var mockIcm) = await CreateClientAndConfigAsync();
            var configuration = new MachineReimagesRule.Configuration(baseConfiguration);
            var rule = new MachineReimagesRule(configuration);
            var ruleContext = new RuleContext(Guid.NewGuid(), SystemClock.Instance.UtcNow, new CancellationToken());

            var results = new object[] {
                new MachineReimagesRule.Result() { Stamp = "S1", Service = Constants.CacheService, Total = 1000, Reimaged = 2 },
                new MachineReimagesRule.Result() { Stamp = "S1", Service = Constants.ContentAddressableStoreService, Total = 1000, Reimaged = 1000 },
                new MachineReimagesRule.Result() { Stamp = "S1", Service = Constants.ContentAddressableStoreMasterService, Total = 3, Reimaged = 3 },

                new MachineReimagesRule.Result() { Stamp = "S2", Service = Constants.CacheService, Total = 1000, Reimaged = 120 },
                new MachineReimagesRule.Result() { Stamp = "S2", Service = Constants.ContentAddressableStoreService, Total = 1000, Reimaged = 1000 },
                new MachineReimagesRule.Result() { Stamp = "S2", Service = Constants.ContentAddressableStoreMasterService, Total = 3, Reimaged = 3 },

                new MachineReimagesRule.Result() { Stamp = "S3", Service = Constants.CacheService, Total = 1000, Reimaged = 230 },
                new MachineReimagesRule.Result() { Stamp = "S3", Service = Constants.ContentAddressableStoreService, Total = 1000, Reimaged = 1000 },
                new MachineReimagesRule.Result() { Stamp = "S3", Service = Constants.ContentAddressableStoreMasterService, Total = 3, Reimaged = 2 },

                new MachineReimagesRule.Result() { Stamp = "S4", Service = Constants.CacheService, Total = 1000, Reimaged = 569 },
                new MachineReimagesRule.Result() { Stamp = "S4", Service = Constants.ContentAddressableStoreService, Total = 1000, Reimaged = 1000 },
                new MachineReimagesRule.Result() { Stamp = "S4", Service = Constants.ContentAddressableStoreMasterService, Total = 3, Reimaged = 0 },
            };

            mockKusto.Add(results);

            await rule.Run(ruleContext);

            // Launcher is currently unsupported. Adjust this test when it is.
            _notifier.Results.Count.Should().Be(0);
        }

        private async Task<(MockKustoClient, MultiStampRuleConfiguration, MockIcmClient)> CreateClientAndConfigAsync(int numStamps = 2)
        {
            var ring = "TEST_RING";
            var cacheTableName = "CloudCacheLogEvent";

            var mockKusto = new MockKustoClient();
            var stamps = new object[numStamps];
            for (var i = 0; i < numStamps; i++)
            {
                stamps[i] = new Watchlist.DynamicStampProperties() { Stamp = $"DM_S{i}", CacheTableName = cacheTableName, Ring = ring };
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
                cacheTableName,
                TestEnvironment,
                watchlist);

            return (mockKusto, baseConfiguration, mockIcm);
        }

        private Task<Watchlist> GetWatchListAsync(MockKustoClient mockKusto)
        {
            var environments = new Dictionary<CloudBuildEnvironment, EnvironmentConfiguration>() {
                {
                    TestEnvironment, Constants.DefaultEnvironments[TestEnvironment]
                }
            };

            var resources = new Dictionary<CloudBuildEnvironment, IKustoClient>()
            {
                {
                    TestEnvironment, mockKusto
                }
            };

            return Watchlist.CreateAsync(TestGlobal.Logger, environments, resources);
        }
    }
}
