// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.Host.Configuration;
using ContentStoreTest.Distributed.Redis;
using FluentAssertions;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Sessions
{
    [Trait("Category", "Integration")]
    [Trait("Category", "LongRunningTest")]
    [Collection("Redis-based tests")]
    [Trait("Category", "WindowsOSOnly")] // 'redis-server' executable no longer exists
#if !NETCOREAPP
    [TestClassIfSupported(TestRequirements.NotSupported)]
#endif
    internal class BlobLocationRegistryDistributedContentTests : LocalLocationStoreDistributedContentTests
    {
        /// <nodoc />
        public BlobLocationRegistryDistributedContentTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(redis, output)
        {
            UseGrpcServer = true;
        }

        // Disable flaky test.
        public override Task PinWithUnverifiedCountAndStartCopyWithThreshold(int threshold)
        {
            return Task.CompletedTask;
        }

        protected override TestDistributedContentSettings ModifySettings(TestDistributedContentSettings dcs)
        {
            if (!dcs.IsMasterEligible)
            {
                // Prevent workers from processing partition to simplify test logging so that
                // partition writes only come from one machine (typically during CreateCheckpointAsync below)
                dcs.LocationStoreSettings.BlobContentLocationRegistrySettings.ProcessPartitions = false;
            }

            dcs.LocationStoreSettings.BlobContentLocationRegistrySettings.PartitionsUpdateInterval = "1m";
            dcs.LocationStoreSettings.BlobContentLocationRegistrySettings.PartitionCount = 2;
            dcs.LocationStoreSettings.BlobContentLocationRegistrySettings.StageInterval = TimeSpan.Zero;
            dcs.LocationStoreSettings.BlobContentLocationRegistrySettings.UpdateInBackground = false;
            dcs.LocationStoreSettings.BlobContentLocationRegistrySettings.UpdateDatabase = true;
            dcs.LocationStoreSettings.EnableBlobContentLocationRegistry = true;
            dcs.GlobalCacheDatabaseValidationMode = DatabaseValidationMode.LogAndError;
            dcs.ContentMetadataUseMergeWrites = true;
            return base.ModifySettings(dcs);
        }

        protected override async Task<BoolResult> RestoreCheckpointAsync(InstanceRef storeRef, TestContext context)
        {
            await base.RestoreCheckpointAsync(storeRef, context).ShouldBeSuccess();

            var index = storeRef.ResolveIndex(context);

            var checkpointManager = context.GetServices(index).Dependencies.GlobalCacheCheckpointManager.GetRequiredInstance();

            await checkpointManager.PeriodicRestoreCheckpointAsync(context).ShouldBeSuccess();

            return BoolResult.Success;
        }

        protected override async Task<BoolResult> CreateCheckpointAsync(InstanceRef storeRef, TestContext context)
        {
            var index = storeRef.ResolveIndex(context);
            index.Should().Be(context.GetMasterIndex());

            await UpdateAllRegistriesAsync(context, index);

            await base.CreateCheckpointAsync(storeRef, context).ShouldBeSuccess();

            var registry = context.GetBlobContentLocationRegistry(index);

            // Increment time to ensure database is updated on master
            TestClock.UtcNow += TimeSpan.FromMinutes(2);
            await registry.UpdatePartitionsAsync(context).ShouldBeSuccess();

            var services = context.GetServices(index);
            var service = context.GetContentMetadataService(index);
            await service.CreateCheckpointAsync(context).ShouldBeSuccess();

            return BoolResult.Success;
        }

        private static async Task UpdateAllRegistriesAsync(TestContext context, int? excludeIndex = null)
        {
            for (int i = 0; i < context.Stores.Count; i++)
            {
                if (i != excludeIndex)
                {
                    var registry = context.GetBlobContentLocationRegistry(i);

                    await registry.UpdatePartitionsAsync(context).ShouldBeSuccess();
                }
            }
        }
    }
}
