// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
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
namespace ContentStoreTest.Stores
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

            var store = new ColdStorage(FileSystem, coldStorageSettings);

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
                 var result = await store.PutFileAsync(context, HashType.MD5, originalPath, FileRealizationMode.HardLink, CancellationToken.None).ShouldBeSuccess();

                 // Hardlink back to original location trying to replace existing.
                 var copyResult = await store.PlaceFileAsync(
                      context,
                      result.ContentHash,
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
                var result = await store.PutFileAsync(context, HashType.MD5, originalPath, FileRealizationMode.Move, CancellationToken.None).ShouldBeSuccess();

                FileSystem.DeleteFile(originalPath);
                FileSystem.FileExists(originalPath).Should().Be(false);

                ContentHashWithPath contentHashWithPath = new ContentHashWithPath(result.ContentHash, originalPath);
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
                await contentSession.PlaceFileAsync(context, result.ContentHash, originalPath, FileAccessMode.Write, FileReplacementMode.FailIfExists, FileRealizationMode.Copy, CancellationToken.None).ShouldBeSuccess();
                FileSystem.FileExists(originalPath).Should().Be(true);
                FileSystem.ReadAllText(originalPath).Should().Be(fileContents);

                _ = await contentSession.ShutdownAsync(context);
                _ = await destination.ShutdownAsync(context);
            });
        }

    }
}
