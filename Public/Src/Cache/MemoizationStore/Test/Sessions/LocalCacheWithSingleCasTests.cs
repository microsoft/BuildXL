// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using FluentAssertions;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using Xunit;
using BuildXL.Cache.ContentStore.Distributed.NuCache;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    public sealed class LocalCacheWithSingleCasTests : LocalCacheTests
    {
        protected override ICache CreateCache(DisposableDirectory testDirectory)
        {
            var rootPath = testDirectory.Path;
            var configuration = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1);
            configuration.Write(FileSystem, rootPath).Wait();

            var memoConfig = new RocksDbMemoizationStoreConfiguration()
            {
                Database = new RocksDbContentLocationDatabaseConfiguration(rootPath)
                {
                    CleanOnInitialize = false,
                    OnFailureDeleteExistingStoreAndRetry = true,
                    LogsKeepLongTerm = true,
                    MetadataGarbageCollectionEnabled = true,
                    MetadataGarbageCollectionMaximumNumberOfEntriesToKeep = MaxContentHashListItems,
                },
            };
            return LocalCache.CreateUnknownContentStoreInProcMemoizationStoreCache(Logger, rootPath, memoConfig, LocalCacheConfiguration.CreateServerDisabled(), clock: Clock);
        }

        [Fact]
        protected override async Task ContentStoreStartupFails()
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                using (var cache = CreateCache(testDirectory))
                {
                    // To cause content store startup to fail, create a file with the same name as an expected directory.
                    FileSystem.WriteAllBytes(testDirectory.Path / "Shared", new byte[] { 0 });

                    var r = await cache.StartupAsync(context);
                    r.ShouldBeError("Content store startup failed");
                }
            }
        }

        [Fact]
        protected override Task MemoizationStoreStartupFails()
        {
            // Left blank on purpose
            return Task.CompletedTask;
        }

        protected override async Task VerifyPinCallCounterBumpedOnUse(ICache cache, Context context)
        {
            long counter = await GetCounterValue("PinCall", cache, context);
            counter.Should().Be(1);
        }

        protected override async Task VerifyOpenStreamCallCounterBumpedOnUse(ICache cache, Context context)
        {
            long counter = await GetCounterValue("OpenStreamCall", cache, context);
            counter.Should().Be(1);
        }

        protected override async Task VerifyPlaceFileCallCounterBumpedOnUse(ICache cache, Context context)
        {
            long counter = await GetCounterValue("PlaceFileCall", cache, context);
            counter.Should().Be(1);
        }

        protected override async Task VerifyPutFileCallCounterBumpedOnUse(ICache cache, Context context)
        {
            long counter = await GetCounterValue("PutFileCall", cache, context);
            counter.Should().Be(1);
        }

        protected override async Task VerifyPutStreamCallCounterBumpedOnUse(ICache cache, Context context)
        {
            long counter = await GetCounterValue("PutStreamCall", cache, context);
            counter.Should().Be(1);
        }
    }
}
