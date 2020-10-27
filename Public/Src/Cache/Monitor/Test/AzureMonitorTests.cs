using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.Monitor.App;
using BuildXL.Cache.Monitor.Library.Az;
using FluentAssertions;
using Microsoft.Azure.Management.Monitor;
using Microsoft.Azure.Management.Monitor.Models;
using Microsoft.Rest.Azure.OData;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.Monitor.Test
{
    public class AzureMonitorTests : TestBase
    {
        private readonly EnvironmentConfiguration _environmentConfiguration = Constants.DefaultEnvironments[CloudBuildEnvironment.Production];

        public AzureMonitorTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact(Skip = "For manual testing only")]
        public async Task CanRunMetricsQueryAsync()
        {
            Debugger.Launch();

            var appKeyResult = GetApplicationKey();
            if (!appKeyResult)
            {
                return;
            }

            var appKey = appKeyResult.ThrowIfFailure();

            var azure = ExternalDependenciesFactory.CreateAzureClient(
                Constants.DefaultAzureTenantId,
                _environmentConfiguration.AzureSubscriptionId,
                Constants.DefaultAzureAppId,
                appKey).ThrowIfFailure();

            var monitorManagementClient = await ExternalDependenciesFactory.CreateAzureMetricsClientAsync(
                Constants.DefaultAzureTenantId,
                _environmentConfiguration.AzureSubscriptionId,
                Constants.DefaultAzureAppId,
                appKey).ThrowIfFailureAsync();

            // var redisCaches = (await azure.RedisCaches.ListAsync()).ToDictionary(cache => cache.Name, cache => cache);

            //"/subscriptions/bf933bbb-8131-491c-81d9-26d7b6f327fa/resourceGroups/CentralUS/providers/Microsoft.Cache/Redis/cbcache-test-redis-dms1"
            // 
            var now = DateTime.UtcNow;

            //var metricDfs = (await monitorManagementClient.MetricDefinitions.ListAsync(resourceUri: "/subscriptions/7965fc55-7602-4cf6-abe4-e081cf119567/resourceGroups/EastUS/providers/Microsoft.Cache/Redis/cbcache-prod-redis-bnps01")).ToList();


            //var result = await monitorManagementClient.Metrics.ListAsync(
            //    resourceUri: "/subscriptions/7965fc55-7602-4cf6-abe4-e081cf119567/resourceGroups/EastUS/providers/Microsoft.Cache/Redis/cbcache-prod-redis-bnps01",
            //    odataQuery: new ODataQuery<MetadataValue>($"name.value eq 'UsedMemory'"),
            //    resultType: ResultType.Data);

            //var result = await monitorManagementClient.Metrics.ListAsync(
            //    resourceUri: "/subscriptions/7965fc55-7602-4cf6-abe4-e081cf119567/resourceGroups/CentralUS/providers/Microsoft.Cache/Redis/cbcache-prod-redis-dmps09",
            //    odataQuery: new ODataQuery<MetadataValue>(odataExpression: "ShardId eq '*'"),
            //    timespan: "2020-10-20T21:00:00.000Z/2020-10-27T21:00:00.000Z",
            //    metricnames: "usedmemory",
            //    aggregation: "maximum, minimum",
            //    //metricnamespace: "microsoft.cache/redis",
            //    orderby: "maximum desc",
            //    resultType: ResultType.Data);

            //var metrics = await monitorManagementClient.GetMetricsAsync(
            //    resourceUri: "/subscriptions/7965fc55-7602-4cf6-abe4-e081cf119567/resourceGroups/EastUS/providers/Microsoft.Cache/Redis/cbcache-prod-redis-bnps01",
            //    metrics: new [] {AzureRedisShardMetric.UsedMemory.ToMetricName()},
            //    startTimeUtc: now - TimeSpan.FromDays(7),
            //    endTimeUtc: now,
            //    samplingInterval: TimeSpan.FromMinutes(5),
            //    aggregations: new[] { Microsoft.Azure.Management.Monitor.Models.AggregationType.Maximum, Microsoft.Azure.Management.Monitor.Models.AggregationType.Minimum }
            //    );

            // usedmemory0, usedmemory1, ..., usedmemory9 (today)
            // operationsPerSecond0, ..., 9 (oct 19th)

            /*
             *
0: {httpMethod: "GET",…}
httpMethod: "GET"
relativeUrl: "/subscriptions/7965fc55-7602-4cf6-abe4-e081cf119567/resourceGroups/CentralUS/providers/Microsoft.Cache/Redis/cbcache-prod-redis-dmps09/providers/microsoft.Insights/metrics?timespan=2020-10-20T21:00:00.000Z/2020-10-27T21:00:00.000Z&interval=FULL&metricnames=usedmemory&aggregation=maximum&metricNamespace=microsoft.cache%2Fredis&top=10&orderby=maximum desc&$filter=ShardId eq '*'&validatedimensions=false&api-version=2019-07-01"
1: {httpMethod: "GET",…}
             *
             */

            // usedmemory per dimension

            var metrics = await monitorManagementClient.GetMetricsWithDimensionAsync(
                resourceUri: "/subscriptions/7965fc55-7602-4cf6-abe4-e081cf119567/resourceGroups/CentralUS/providers/Microsoft.Cache/Redis/cbcache-prod-redis-dmps09",
                metrics: new[] { AzureRedisShardMetric.UsedMemory.ToMetricName() },
                dimension: "ShardId",
                startTimeUtc: now - TimeSpan.FromDays(7),
                endTimeUtc: now,
                samplingInterval: TimeSpan.FromMinutes(5),
                aggregations: new[] { Microsoft.Azure.Management.Monitor.Models.AggregationType.Maximum, Microsoft.Azure.Management.Monitor.Models.AggregationType.Minimum }
            );

            metrics.Count.Should().Be(20);
        }
    }
}
