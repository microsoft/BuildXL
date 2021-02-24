// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Monitor.App.Rules.Autoscaling;
using BuildXL.Cache.Monitor.Library.Rules.Autoscaling;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

#nullable enable

namespace BuildXL.Cache.Monitor.Test
{
    public class RedisAutoscalerTests : TestBase
    {
        public RedisAutoscalerTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public Task DisallowsLowCostDownscalesAsync()
        {
            return RunTestAsync(async (operationContext, redisAutoscalingAgent) =>
            {
                var redisInstance = new MockRedisInstance(RedisClusterSize.Parse("P2/1"));
                redisAutoscalingAgent.UsedMemoryBytes.Add("7.4 GB".ToSize());
                redisAutoscalingAgent.OperationsPerSecond.Add(10000);

                var modelOutput = await redisAutoscalingAgent
                    .EstimateBestClusterSizeAsync(operationContext, redisInstance)
                    .ThrowIfFailureAsync();

                modelOutput
                    .ScalePath
                    .Should()
                    .BeEmpty();
            });
        }

        [Theory]
        [InlineData("P1/1", new[] { "P1/2" }, "5.4 GB")]
        [InlineData("P1/2", new[] { "P1/3" }, "11 GB")]
        [InlineData("P1/3", new[] { "P1/4" }, "16 GB")]
        [InlineData("P1/4", new[] { "P1/6" }, "22 GB")]
        public Task PrefersAddingShardsWhenMemoryGrowsAsync(string initialClusterSize, IEnumerable<string> expectedPath, string usedMemoryAcrossAllShards)
        {
            return RunTestAsync(async (operationContext, redisAutoscalingAgent) =>
            {
                redisAutoscalingAgent.UsedMemoryBytes.Add(usedMemoryAcrossAllShards.ToSize());
                redisAutoscalingAgent.OperationsPerSecond.Add(10);

                var redisInstance = new MockRedisInstance(RedisClusterSize.Parse(initialClusterSize));
                var modelOutput = await redisAutoscalingAgent
                    .EstimateBestClusterSizeAsync(operationContext, redisInstance)
                    .ThrowIfFailureAsync();

                modelOutput
                    .ScalePath
                    .Should()
                    .BeEquivalentTo(expectedPath.Select(size => RedisClusterSize.Parse(size)));
            });
        }

        [Theory]
        // Memory load is low and server load is high.
        [InlineData("P1/1", new string[] { }, "3 GB", 60)]
        // Memory load is low and server load is extremely high. TODO: we should probably be upscaling in these cases,
        // however, they don't happen often in our prod env.
        [InlineData("P1/1", new string[] { }, "3 GB", 80)]
        // Memory load is high and server load is low.
        [InlineData("P1/1", new[] { "P1/2" }, "5.4 GB", 10)]
        // Memory load is high and server load is high.
        [InlineData("P1/1", new[] { "P1/2" }, "5.4 GB", 60)]
        // Memory load is high and server load is extremely high.
        [InlineData("P1/1", new[] { "P2/1" }, "5.4 GB", 80)]
        public Task ChangesSKUWhenServerLoadIsHigh(string initialClusterSize, IEnumerable<string> expectedPath, string usedMemoryAcrossAllShards, int serverLoadPctAcrossAllShards)
        {
            return RunTestAsync(async (operationContext, redisAutoscalingAgent) =>
            {
                redisAutoscalingAgent.UsedMemoryBytes.Add(usedMemoryAcrossAllShards.ToSize());
                redisAutoscalingAgent.OperationsPerSecond.Add(10);
                redisAutoscalingAgent.MaximumServerLoadAcrossShards.Add(serverLoadPctAcrossAllShards);

                var redisInstance = new MockRedisInstance(RedisClusterSize.Parse(initialClusterSize));
                var modelOutput = await redisAutoscalingAgent
                    .EstimateBestClusterSizeAsync(operationContext, redisInstance)
                    .ThrowIfFailureAsync();

                modelOutput
                    .ScalePath
                    .Should()
                    .BeEquivalentTo(expectedPath.Select(size => RedisClusterSize.Parse(size)));
            });
        }

        private async Task RunTestAsync(Func<OperationContext, TestableRedisAutoscalingAgent, Task> testFunc)
        {
            // Console is forwarded to XUnit as per TestBase
            using var consoleLog = new ConsoleLog();
            using var logger = new Logger(new[] { consoleLog });
            var configuration = new RedisAutoscalingAgent.Configuration();
            var redisAutoscalingAgent = new TestableRedisAutoscalingAgent(configuration);

            var tracingContext = new Context(logger);
            var operationContext = new OperationContext(tracingContext, token: default);
            await testFunc(operationContext, redisAutoscalingAgent);
        }

        /// <summary>
        /// This class removes the aspect of fetching metrics from Azure Metrics in an easy way. The issue here is that
        /// it's bothersome to simulate fetching metrics from the <see cref="MockAzureMetricsClient"/> due to
        /// concurrency.
        /// </summary>
        private class TestableRedisAutoscalingAgent : RedisAutoscalingAgent
        {
            public TestableRedisAutoscalingAgent(Configuration configuration)
                : base(configuration, new MockAzureMetricsClient())
            {
            }

            public List<double> UsedMemoryBytes { get; set; } = new List<double>();

            public List<double> OperationsPerSecond { get; set; } = new List<double>();

            /// <remarks>
            /// This is defaulted to a single element list because we need to ensure that there's at least one element.
            /// </remarks>
            public List<double> MaximumServerLoadAcrossShards { get; set; } = new List<double>();

            protected override Task<List<double>> FetchMemoryUsedPerShardAsync(
                OperationContext context,
                DateTime now,
                string redisAzureId)
            {
                return Task.FromResult(UsedMemoryBytes);
            }

            protected override Task<List<double>> FetchOperationsPerSecondPerShardAsync(
                OperationContext context,
                DateTime now,
                string redisAzureId)
            {
                return Task.FromResult(OperationsPerSecond);
            }

            protected override Task<List<double>> FetchMaximumServerLoadAcrossShardsAsync(
                OperationContext context,
                DateTime now,
                string redisAzureId)
            {
                return Task.FromResult(MaximumServerLoadAcrossShards);
            }
        }
    }
}
