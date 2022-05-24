// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.Host.Configuration;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

// ReSharper disable All
namespace ContentStoreTest.Distributed.Stores
{
    public class ColdStorageTests : TestBase
    {

        protected const string Name = "name";

        private static readonly ITestClock Clock = new MemoryClock();

        private readonly static Random Random = new Random();

        public ColdStorageTests()
            : base(() => new MemoryFileSystem(Clock), TestGlobal.Logger)
        {
        }

        protected async Task RunTestAsync(Func<OperationContext, ColdStorage, DisposableDirectory, Task> funcAsync)
        {
            var context = new OperationContext(new Context(Logger));
            var directory = new DisposableDirectory(FileSystem);

            ColdStorageSettings coldStorageSettings = new ColdStorageSettings(directory.Path.Path, "1MB");

            TestDistributedContentCopier copier = DistributedContentCopierTests.CreateMocks(
                new MemoryFileSystem(TestSystemClock.Instance),
                directory.CreateRandomFileName(),
                TimeSpan.FromSeconds(1)).Item1;

            var store = new ColdStorage(FileSystem, coldStorageSettings, copier);

            try
            {
                Assert.False(store.StartupStarted);
                Assert.False(store.StartupCompleted);
                Assert.False(store.ShutdownStarted);
                Assert.False(store.ShutdownCompleted);

                await store.StartupAsync(context).ShouldBeSuccess();

                await funcAsync(context, store, directory);
            }
            finally
            {
                await store.ShutdownAsync(context).ShouldBeSuccess();
            }

            Assert.True(store.StartupStarted);
            Assert.True(store.StartupCompleted);
            Assert.True(store.ShutdownStarted);
            Assert.True(store.ShutdownCompleted);
        }

        private static string GetRandomFileContents()
        {
            var bytes = new byte[FileSystemDefaults.DefaultFileStreamBufferSize + 1];
            Random.NextBytes(bytes);
            return Encoding.UTF8.GetString(bytes);
        }

        [Fact]
        public Task TestColdStorage()
        {
            return RunTestAsync(async (context, store, directory) =>
             {
                 var originalPath = directory.Path / "original.txt";
                 var fileContents = GetRandomFileContents();

                 // Create the file and hardlink it into the cache.
                 FileSystem.WriteAllText(originalPath, fileContents);

                 var contentHasher = HashInfoLookup.GetContentHasher(HashType.MD5);
                 var contentHash = contentHasher.GetContentHash(Encoding.UTF8.GetBytes(fileContents));

                 await store.PutFileAsync(context, contentHash, new DisposableFile(context, FileSystem, originalPath), context.Token).ShouldBeSuccess();

                 // Hardlink back to original location trying to replace existing.
                 var copyResult = await store.PlaceFileAsync(
                      context,
                      contentHash,
                      originalPath,
                      FileAccessMode.ReadOnly,
                      FileReplacementMode.ReplaceExisting,
                      FileRealizationMode.Any,
                      CancellationToken.None).ShouldBeSuccess();

                  // The file is intact.
                  FileSystem.ReadAllText(originalPath).Should().Be(fileContents);
              });
        }

        [Fact]
        public Task TestColdStorageWithBulkFunction()
        {
            return RunTestAsync(async (context, store, directory) =>
            {
                var originalPath = directory.Path / "original.txt";
                var fileContents = GetRandomFileContents();

                // Build destination IContentSession
                DisposableDirectory sessionDirectory = new DisposableDirectory(FileSystem);
                ConfigurationModel configurationModel = new ConfigurationModel(new ContentStoreConfiguration(new MaxSizeQuota("10MB")));
                FileSystemContentStore destination = new FileSystemContentStore(FileSystem, SystemClock.Instance, sessionDirectory.Path, configurationModel);
                _ = await destination.StartupAsync(context);
                IContentSession contentSession = destination.CreateSession(context, "test_session", BuildXL.Cache.ContentStore.Interfaces.Stores.ImplicitPin.None).Session;
                _ = await contentSession.StartupAsync(context);

                // Create the file and hardlink it into the cache.
                FileSystem.WriteAllText(originalPath, fileContents);

                var contentHasher = HashInfoLookup.GetContentHasher(HashType.MD5);
                var contentHash = contentHasher.GetContentHash(Encoding.UTF8.GetBytes(fileContents));

                await store.PutFileAsync(context, contentHash, new DisposableFile(context, FileSystem, originalPath), context.Token).ShouldBeSuccess();

                FileSystem.DeleteFile(originalPath);
                FileSystem.FileExists(originalPath).Should().Be(false);

                ContentHashWithPath contentHashWithPath = new ContentHashWithPath(contentHash, originalPath);
                List<ContentHashWithPath> listFile = new List<ContentHashWithPath>();
                listFile.Add(contentHashWithPath);

                // Hardlink back to original location trying to replace existing.
                var copyTask = await store.FetchThenPutBulkAsync(
                     context,
                     listFile,
                     contentSession);

                await copyTask.ToLookupAwait(r => { return r.Item.Succeeded; });

                FileSystem.FileExists(originalPath).Should().Be(false);

                // The file is in the destination.
                await contentSession.PlaceFileAsync(context, contentHash, originalPath, FileAccessMode.Write, FileReplacementMode.FailIfExists, FileRealizationMode.Copy, CancellationToken.None).ShouldBeSuccess();
                FileSystem.FileExists(originalPath).Should().Be(true);
                FileSystem.ReadAllText(originalPath).Should().Be(fileContents);

                _ = await contentSession.ShutdownAsync(context);
                _ = await destination.ShutdownAsync(context);
            });
        }

        [Fact]
        public Task TestUpdateRingAndGetLocations()
        {
            return RunTestAsync(async (context, store, directory) =>
            {
                ClusterState clusterState = ClusterState.CreateForTest();
                for (int i = 0; i <= 10; i++)
                {
                    var machineId = new MachineId(i);
                    var machineLocation = new MachineLocation(i.ToString());
                    clusterState.AddMachineForTest(context, machineId, machineLocation);
                }

                var contentHashes = new List<ContentHashWithPath>();

                var contentHash1 = new ContentHashWithPath(new ContentHash("MD5:72F6F256239CC69B6FE9AF1C7489CFD1"), directory.Path / "hash1");
                var contentHash2 = new ContentHashWithPath(new ContentHash("MD5:61C4F184221AD54A2FA4A92A6137AA42"), directory.Path / "hash2");
                contentHashes.Add(contentHash1);
                contentHashes.Add(contentHash2);

                var result = await store.UpdateRingAsync(context, clusterState).ShouldBeSuccess();
                var locations = store.GetBulkLocations(context, contentHashes).ShouldBeSuccess();

                locations.Count.Should().Be(2);

                locations.ContentHashesInfo[0].ContentHash.Serialize().Should().Be("MD5:72F6F256239CC69B6FE9AF1C7489CFD1");
                locations.ContentHashesInfo[0].Locations[0].ToString().Should().Be("6");
                locations.ContentHashesInfo[0].Locations[1].ToString().Should().Be("4");
                locations.ContentHashesInfo[0].Locations[2].ToString().Should().Be("5");

                locations.ContentHashesInfo[1].ContentHash.Serialize().Should().Be("MD5:61C4F184221AD54A2FA4A92A6137AA42");
                locations.ContentHashesInfo[1].Locations[0].ToString().Should().Be("10");
                locations.ContentHashesInfo[1].Locations[1].ToString().Should().Be("1");
                locations.ContentHashesInfo[1].Locations[2].ToString().Should().Be("3");
            });
        }

        [Fact]
        public Task TestPutToColdStorageWithRemoteLocations()
        {
            return RunTestAsync(async (context, store, directory) =>
            {
                ClusterState clusterState = ClusterState.CreateForTest();
                for (int i = 0; i <= 10; i++)
                {
                    clusterState.AddMachineForTest(context, new MachineId(i), new MachineLocation(i.ToString()));
                }
                var result = await store.UpdateRingAsync(context, clusterState).ShouldBeSuccess();

                // Create file to put
                var originalPath = directory.Path / "original.txt";
                var fileContents = GetRandomFileContents();

                FileSystem.WriteAllText(originalPath, fileContents);

                var contentHasher = HashInfoLookup.GetContentHasher(HashType.MD5);
                var contentHash = contentHasher.GetContentHash(Encoding.UTF8.GetBytes(fileContents));

                await store.PutFileAsync(context, contentHash, new DisposableFile(context, FileSystem, originalPath), context.Token).ShouldBeSuccess();
            });
        }

    }
}
