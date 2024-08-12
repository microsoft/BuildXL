// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.AdoBuildRunner;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.Tool.AdoBuildRunner
{
    /// <summary>
    /// Tests to verify the functionality of cache configuration generation.
    /// </summary>
    public class CacheConfigurationGenerationTests : TemporaryStorageTestBase
    {
        /// <summary>
        /// Tests that the correct cache configuration is created for the various cache types available.
        /// </summary>
        [Theory]
        [InlineData(CacheType.Blob, CacheConfigJsonResults.BlobCacheConfigWithCacheResourceAndNoCacheResourceName, "")]
        [InlineData(CacheType.Blob, CacheConfigJsonResults.BlobCacheConfigWithCacheResourceAndCacheResourceName, "MyCacheResource")]
        [InlineData(CacheType.EphemeralBuildWide, CacheConfigJsonResults.EphemeralCacheConfigWithCacheResourceAndNoCacheResourceName, "")]
        [InlineData(CacheType.EphemeralBuildWide, CacheConfigJsonResults.EphemeralCacheConfigWithCacheResourceAndCacheResourceName, "MyCacheResource")]
        public void GenerateCacheConfigTestWithCacheResource(CacheType cacheType, string expectedCacheConfigResult, string cacheResourceName)
        {
            var cacheConfiguration = new MockCacheConfigGeneration();
            cacheConfiguration.CacheType = cacheType;
            cacheConfiguration.HostedPoolActiveBuildCacheName = cacheResourceName;

            // Create cache resource config file.
            var hostedPoolBuildCacheConfigurationFilePath = Path.Combine(TemporaryDirectory, "cacheResourceFile.txt");
            if (File.Exists(hostedPoolBuildCacheConfigurationFilePath))
            {
                File.Delete(hostedPoolBuildCacheConfigurationFilePath);
            }
            File.WriteAllText(hostedPoolBuildCacheConfigurationFilePath, "cacheResourceFile");
            File.Exists(hostedPoolBuildCacheConfigurationFilePath);
            cacheConfiguration.HostedPoolBuildCacheConfigurationFile = hostedPoolBuildCacheConfigurationFilePath;
            // We need to dynamically replace the cache config file path in the expected json string.
            var expectedCacheConfig = expectedCacheConfigResult.Replace("{File_path}", hostedPoolBuildCacheConfigurationFilePath.Replace(@"\", @"\\"))
                                                                .Replace("\r\n", "\n")
                                                                .Trim();


            XAssert.AreEqual(expectedCacheConfig, CacheConfigGenerator.GenerateCacheConfig(cacheConfiguration).Replace("\r\n", "\n").Trim());
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
            cacheConfiguration.CacheType = cacheType;
            XAssert.AreEqual(expectedCacheConfigResult.Replace("\r\n", "\n").Trim(), CacheConfigGenerator.GenerateCacheConfig(cacheConfiguration).Replace("\r\n", "\n").Trim());
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
    ""HostedPoolBuildCacheConfigurationFile"": ""{File_path}""
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
    ""HostedPoolActiveBuildCacheName"": ""MyCacheResource""
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
  ""HostedPoolBuildCacheConfigurationFile"": ""{File_path}""
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
  ""HostedPoolActiveBuildCacheName"": ""MyCacheResource""
}";
        }
    }
}