// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.VerticalAggregator;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace BuildXL.Cache.Tests
{
    /// <summary>
    /// This class allows the VerticalAggregator to be tested as a standard cache.
    /// </summary>
    /// <remarks>
    /// This lives with the InMemory tests as we need their JSON to successfully construct the tests,
    /// and putting them in the VerticalAggretator test class would create a circular dependency.
    /// </remarks>
    public class VerticalAggregatorInstanceTests : TestCacheBasicTests
    {
        public override string NewCache(string cacheId, bool strictMetadataCasCoupling, bool authoritative = false)
        {
            string localConfig = new TestInMemory().NewCache(cacheId + VerticalAggregatorBaseTests.LocalMarker, false);
            string remoteConfig = new TestInMemory().NewCache(cacheId + VerticalAggregatorBaseTests.RemoteMarker, true, authoritative: true);

            string configString = VerticalAggregatorBaseTests.NewCacheString(cacheId, localConfig, remoteConfig, false, false, false);

            return configString;
        }

        public override async Task<ICache> CreateCacheAsync(string cacheId, bool strictMetadataCasCoupling = true)
        {
            string cacheConfigData = NewCache(cacheId, strictMetadataCasCoupling);

            Possible<ICache, Failure> cachePossible = await InitializeCacheAsync(cacheConfigData);

            ICache cache = cachePossible.Success();
            XAssert.AreEqual(GetCacheFormattedId(cacheId), cache.CacheId);

            return cache;
        }

        protected override string GetCacheFormattedId(string cacheId)
        {
            return cacheId + VerticalAggregatorBaseTests.LocalMarker + "_" + cacheId + VerticalAggregatorBaseTests.RemoteMarker;
        }

        protected override IEnumerable<EventSource> EventSources => new EventSource[0];

        protected virtual string ReadOnly => "false";

        [Fact]
        public async Task FailToCreateL2CacheWorks()
        {
            const string TestName = "FailToCreateL2CacheWorks";
            string testCacheId = MakeCacheId(TestName);

            string localConfig = new TestInMemory().NewCache(testCacheId + VerticalAggregatorBaseTests.LocalMarker, false);
            string remoteConfig = new TestInMemory().NewCacheFailure(testCacheId + VerticalAggregatorBaseTests.RemoteMarker, true, authoritative: true);

            string cacheConfigData = VerticalAggregatorBaseTests.NewCacheString(testCacheId, localConfig, remoteConfig, false, false, false, false);

            ICache cache = await InitializeCacheAsync(cacheConfigData).SuccessAsync();

            bool warningSent = false;
            string warningMessage = string.Empty;
            string expectedWarning = string.Format(System.Globalization.CultureInfo.InvariantCulture, VerticalCacheAggregatorFactory.RemoteConstructionFailureWarning, testCacheId + VerticalAggregatorBaseTests.LocalMarker);

            cache.SuscribeForCacheStateDegredationFailures((failure) => { warningSent = true;
                warningMessage = failure.Describe(); });

            XAssert.IsTrue(warningSent);

            XAssert.IsNull(cache as VerticalCacheAggregator, "We should have eliminated the VerticalCacheAggregator as there is only 1 operating cache");

            XAssert.AreEqual(expectedWarning, warningMessage);

            await cache.ShutdownAsync().SuccessAsync();
        }


        [Fact]
        public async Task FailToCreateL2CacheFails()
        {
            const string TestName = "FailToCreateL2CacheFails";
            string testCacheId = MakeCacheId(TestName);

            string localConfig = new TestInMemory().NewCache(testCacheId + VerticalAggregatorBaseTests.LocalMarker, false);
            string remoteConfig = new TestInMemory().NewCacheFailure(testCacheId + VerticalAggregatorBaseTests.RemoteMarker, true, authoritative: true);

            string cacheConfigData = VerticalAggregatorBaseTests.NewCacheString(testCacheId, localConfig, remoteConfig, false, false, false, true);

            Possible<ICache, Failure> cachePossible = await InitializeCacheAsync(cacheConfigData);

            XAssert.IsFalse(cachePossible.Succeeded, "This should have failed cache construction");
        }

        [Fact]
        public async Task FailToCreateL1CacheFails()
        {
            const string TestName = "FailToCreateL1CacheFails";
            string testCacheId = MakeCacheId(TestName);

            string localConfig = new TestInMemory().NewCacheFailure(testCacheId + VerticalAggregatorBaseTests.LocalMarker, false);
            string remoteConfig = new TestInMemory().NewCache(testCacheId + VerticalAggregatorBaseTests.RemoteMarker, true, authoritative: true);

            string cacheConfigData = VerticalAggregatorBaseTests.NewCacheString(testCacheId, localConfig, remoteConfig, false, false, false, false);

            Possible<ICache, Failure> cachePossible = await InitializeCacheAsync(cacheConfigData);

            XAssert.IsFalse(cachePossible.Succeeded, "This should have failed cache construction");
        }
    }

}
