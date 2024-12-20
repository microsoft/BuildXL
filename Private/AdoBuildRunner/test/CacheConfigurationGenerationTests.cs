// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner;
using BuildXL.AdoBuildRunner.Vsts;
using Xunit;

namespace Test.Tool.AdoBuildRunner
{
    /// <summary>
    /// Tests to verify the functionality of cache configuration generation.
    /// </summary>
    public class CacheConfigurationGenerationTests : TestBase
    {
        /// <summary>
        /// Tests that the correct cache configuration is created for the various cache types available.
        /// </summary>
        [Theory]
        [InlineData(CacheType.Blob, CacheConfigJsonResults.BlobCacheConfigWithCacheResourceAndNoCacheResourceName, "")]
        [InlineData(CacheType.Blob, CacheConfigJsonResults.BlobCacheConfigWithCacheResourceAndCacheResourceName, "MyCacheResource")]
        [InlineData(CacheType.EphemeralBuildWide, CacheConfigJsonResults.EphemeralCacheConfigWithCacheResourceAndNoCacheResourceName, "")]
        [InlineData(CacheType.EphemeralBuildWide, CacheConfigJsonResults.EphemeralCacheConfigWithCacheResourceAndCacheResourceName, "MyCacheResource")]
        public async Task GenerateCacheConfigTestWithCacheResource(CacheType cacheType, string expectedCacheConfigResult, string cacheResourceName)
        {
            var cacheConfiguration = new MockCacheConfigGeneration();
            cacheConfiguration.StorageAccountEndpoint = null;
            cacheConfiguration.CacheType = cacheType;
            cacheConfiguration.HostedPoolActiveBuildCacheName = cacheResourceName;

            // Create cache resource config file.
            var hostedPoolBuildCacheConfigurationFilePath = Path.Combine(TemporaryDirectory, "cacheResourceFile.txt");
            if (File.Exists(hostedPoolBuildCacheConfigurationFilePath))
            {
                File.Delete(hostedPoolBuildCacheConfigurationFilePath);
            }
            await File.WriteAllTextAsync(hostedPoolBuildCacheConfigurationFilePath, "cacheResourceFile");
            File.Exists(hostedPoolBuildCacheConfigurationFilePath);
            cacheConfiguration.HostedPoolBuildCacheConfigurationFile = hostedPoolBuildCacheConfigurationFilePath;
            // We need to dynamically replace the cache config file path in the expected json string.
            var expectedCacheConfig = expectedCacheConfigResult.Replace("{File_path}", hostedPoolBuildCacheConfigurationFilePath.Replace(@"\", @"\\"))
                                                                .Replace("\r\n", "\n")
                                                                .Trim();


            Assert.Equal(expectedCacheConfig, CacheConfigGenerator.GenerateCacheConfig(cacheConfiguration).Replace("\r\n", "\n").Trim());
        }

        /// <summary>
        /// Tests that the correct cache configuration is created for the various cache types available.
        /// </summary>
        [Theory]
        [InlineData(CacheType.Blob, CacheConfigJsonResults.BlobCacheConfigWithoutCacheResource)]
        [InlineData(CacheType.EphemeralBuildWide, CacheConfigJsonResults.EphemeralCacheConfigWithoutCacheResource)]
        public void GenerateCacheConfigTestWithoutCacheResource(CacheType cacheType, string expectedCacheConfigResult)
        {
            var cacheConfiguration = new MockCacheConfigGeneration();
            cacheConfiguration.HostedPoolBuildCacheConfigurationFile = null;
            cacheConfiguration.CacheType = cacheType;
            Assert.Equal(expectedCacheConfigResult.Replace("\r\n", "\n").Trim(), CacheConfigGenerator.GenerateCacheConfig(cacheConfiguration).Replace("\r\n", "\n").Trim());
        }

        [Theory]
        [InlineData("https://contoso.com", true, true)]
        [InlineData("https://contoso.com", false, true)]
        [InlineData(null, true, true)]
        [InlineData(null, false, false)]
        public async Task TestShouldGenerateCacheConfig(string? storageAccountEndpoint, bool hostedPoolBuildCacheConfigurationFilePathIsPresent, bool expectCacheConfigIsGenerated)
        {
            var cacheConfiguration = new MockCacheConfigGeneration();
            cacheConfiguration.StorageAccountEndpoint = storageAccountEndpoint == null ? null : new System.Uri(storageAccountEndpoint);

            if (hostedPoolBuildCacheConfigurationFilePathIsPresent)
            {
                var hostedPoolBuildCacheConfigurationFilePath = Path.Combine(TemporaryDirectory, "buildCacheConfig.json");
                await File.WriteAllTextAsync(hostedPoolBuildCacheConfigurationFilePath, "mock file");
                cacheConfiguration.HostedPoolBuildCacheConfigurationFile = hostedPoolBuildCacheConfigurationFilePath;
            }
            else
            {
                cacheConfiguration.HostedPoolBuildCacheConfigurationFile = null;
            }

            var args = new List<string>();
            var logger = new Logger();

            await AdoBuildRunnerService.GenerateCacheConfigFileIfNeededAsync(logger, cacheConfiguration, args, Path.Combine(TemporaryDirectory, "cacheConfig.json"));

            // If the cache config is generated the corresponding argument is injected, so we check for that
            Assert.Equal(expectCacheConfigIsGenerated, args.Count > 0);
        }

        [Fact]
        public void TestStorageAccountEndpointTrumpsHostedPoolBuildCache()
        {
            var cacheConfiguration = new MockCacheConfigGeneration();

            // Set both the storage account endpoint and the path to the hosted pool build cache file. The account endpoint should win.
            cacheConfiguration.StorageAccountEndpoint = new Uri("https://test.cacheresource.com");
            cacheConfiguration.HostedPoolBuildCacheConfigurationFile = Path.Combine(TemporaryDirectory, "buildCacheConfig.json");

            var generatedConfig = CacheConfigGenerator.GenerateCacheConfig(cacheConfiguration);

            // The generated config should contain the storage account endpoint but not the hosted pool cache file
            AssertNotContains(generatedConfig, "HostedPoolBuildCacheConfigurationFile");
            AssertContains(generatedConfig, "StorageAccountEndpoint");
        }

        /// <summary>
        /// Test data for CacheConfig unit tests.
        /// It represents the expected cache config for each cache type.
        /// </summary>
        internal static class CacheConfigJsonResults
        {
            public const string BlobCacheConfigWithoutCacheResource = @"{
  ""RemoteIsReadOnly"": false,
  ""SkipDeterminismRecovery"": true,
  ""WriteThroughCasData"": true,
  ""FailIfRemoteFails"": true,
  ""RemoteConstructionTimeoutMilliseconds"": 30000,
  ""Assembly"": ""BuildXL.Cache.VerticalAggregator"",
  ""Type"": ""BuildXL.Cache.VerticalAggregator.VerticalCacheAggregatorFactory"",
  ""RemoteCache"": {
    ""Assembly"": ""BuildXL.Cache.MemoizationStoreAdapter"",
    ""CacheLogPath"": ""[BuildXLSelectedLogPath].Remote.log"",
    ""Type"": ""BuildXL.Cache.MemoizationStoreAdapter.BlobCacheFactory"",
    ""CacheId"": ""12345Remote"",
    ""Universe"": ""MyCacheUniverse"",
    ""RetentionPolicyInDays"": 30,
    ""StorageAccountEndpoint"": ""https://test.cacheresource.com/"",
    ""ManagedIdentityId"": ""00000000-0000-0000-0000-000000000000""
  },
  ""LocalCache"": {
    ""MaxCacheSizeInMB"": 200,
    ""Assembly"": ""BuildXL.Cache.MemoizationStoreAdapter"",
    ""UseStreamCAS"": false,
    ""Type"": ""BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory"",
    ""CacheLogPath"": ""[BuildXLSelectedLogPath]"",
    ""CacheRootPath"": ""[BuildXLSelectedRootPath]"",
    ""CacheId"": ""12345Local"",
    ""ImplicitPin"": 0
  }
}";

            public const string BlobCacheConfigWithCacheResourceAndNoCacheResourceName = @"{
  ""RemoteIsReadOnly"": false,
  ""SkipDeterminismRecovery"": true,
  ""WriteThroughCasData"": true,
  ""FailIfRemoteFails"": true,
  ""RemoteConstructionTimeoutMilliseconds"": 30000,
  ""Assembly"": ""BuildXL.Cache.VerticalAggregator"",
  ""Type"": ""BuildXL.Cache.VerticalAggregator.VerticalCacheAggregatorFactory"",
  ""RemoteCache"": {
    ""Assembly"": ""BuildXL.Cache.MemoizationStoreAdapter"",
    ""CacheLogPath"": ""[BuildXLSelectedLogPath].Remote.log"",
    ""Type"": ""BuildXL.Cache.MemoizationStoreAdapter.BlobCacheFactory"",
    ""CacheId"": ""12345Remote"",
    ""HostedPoolBuildCacheConfigurationFile"": ""{File_path}"",
    ""ConnectionStringFileDataProtectionEncrypted"": ""false""
  },
  ""LocalCache"": {
    ""MaxCacheSizeInMB"": 200,
    ""Assembly"": ""BuildXL.Cache.MemoizationStoreAdapter"",
    ""UseStreamCAS"": false,
    ""Type"": ""BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory"",
    ""CacheLogPath"": ""[BuildXLSelectedLogPath]"",
    ""CacheRootPath"": ""[BuildXLSelectedRootPath]"",
    ""CacheId"": ""12345Local"",
    ""ImplicitPin"": 0
  }
}";

            public const string BlobCacheConfigWithCacheResourceAndCacheResourceName = @"{
  ""RemoteIsReadOnly"": false,
  ""SkipDeterminismRecovery"": true,
  ""WriteThroughCasData"": true,
  ""FailIfRemoteFails"": true,
  ""RemoteConstructionTimeoutMilliseconds"": 30000,
  ""Assembly"": ""BuildXL.Cache.VerticalAggregator"",
  ""Type"": ""BuildXL.Cache.VerticalAggregator.VerticalCacheAggregatorFactory"",
  ""RemoteCache"": {
    ""Assembly"": ""BuildXL.Cache.MemoizationStoreAdapter"",
    ""CacheLogPath"": ""[BuildXLSelectedLogPath].Remote.log"",
    ""Type"": ""BuildXL.Cache.MemoizationStoreAdapter.BlobCacheFactory"",
    ""CacheId"": ""12345Remote"",
    ""HostedPoolBuildCacheConfigurationFile"": ""{File_path}"",
    ""HostedPoolActiveBuildCacheName"": ""MyCacheResource"",
    ""ConnectionStringFileDataProtectionEncrypted"": ""false""
  },
  ""LocalCache"": {
    ""MaxCacheSizeInMB"": 200,
    ""Assembly"": ""BuildXL.Cache.MemoizationStoreAdapter"",
    ""UseStreamCAS"": false,
    ""Type"": ""BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory"",
    ""CacheLogPath"": ""[BuildXLSelectedLogPath]"",
    ""CacheRootPath"": ""[BuildXLSelectedRootPath]"",
    ""CacheId"": ""12345Local"",
    ""ImplicitPin"": 0
  }
}";

            public const string EphemeralCacheConfigWithoutCacheResource = @"{
  ""Type"": ""BuildXL.Cache.MemoizationStoreAdapter.EphemeralCacheFactory"",
  ""Assembly"": ""BuildXL.Cache.MemoizationStoreAdapter"",
  ""CacheId"": ""12345"",
  ""CacheLogPath"": ""[BuildXLSelectedLogPath].Ephemeral.log"",
  ""CacheRootPath"": ""[BuildXLSelectedRootPath]"",
  ""LeaderMachineName"": ""[BuildXLSelectedLeader]"",
  ""DatacenterWide"": ""false"",
  ""CacheSizeMb"": 200,
  ""Universe"": ""MyCacheUniverse"",
  ""RetentionPolicyInDays"": 30,
  ""StorageAccountEndpoint"": ""https://test.cacheresource.com/"",
  ""ManagedIdentityId"": ""00000000-0000-0000-0000-000000000000""
}";

            public const string EphemeralCacheConfigWithCacheResourceAndNoCacheResourceName = @"{
  ""Type"": ""BuildXL.Cache.MemoizationStoreAdapter.EphemeralCacheFactory"",
  ""Assembly"": ""BuildXL.Cache.MemoizationStoreAdapter"",
  ""CacheId"": ""12345"",
  ""CacheLogPath"": ""[BuildXLSelectedLogPath].Ephemeral.log"",
  ""CacheRootPath"": ""[BuildXLSelectedRootPath]"",
  ""LeaderMachineName"": ""[BuildXLSelectedLeader]"",
  ""DatacenterWide"": ""false"",
  ""CacheSizeMb"": 200,
  ""HostedPoolBuildCacheConfigurationFile"": ""{File_path}"",
  ""ConnectionStringFileDataProtectionEncrypted"": ""false""
}";

            public const string EphemeralCacheConfigWithCacheResourceAndCacheResourceName = @"{
  ""Type"": ""BuildXL.Cache.MemoizationStoreAdapter.EphemeralCacheFactory"",
  ""Assembly"": ""BuildXL.Cache.MemoizationStoreAdapter"",
  ""CacheId"": ""12345"",
  ""CacheLogPath"": ""[BuildXLSelectedLogPath].Ephemeral.log"",
  ""CacheRootPath"": ""[BuildXLSelectedRootPath]"",
  ""LeaderMachineName"": ""[BuildXLSelectedLeader]"",
  ""DatacenterWide"": ""false"",
  ""CacheSizeMb"": 200,
  ""HostedPoolBuildCacheConfigurationFile"": ""{File_path}"",
  ""HostedPoolActiveBuildCacheName"": ""MyCacheResource"",
  ""ConnectionStringFileDataProtectionEncrypted"": ""false""
}";
        }
    }
}