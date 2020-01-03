// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using ContentStoreTest.Extensions;
using ContentStoreTest.Test;
using FluentAssertions;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace ContentStoreTest.Distributed.Stores
{
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)] // needs local redis-server.exe
    public class GrpcCopyContentTests : TestBase
    {
        private const int FileSize = 1000;
        private const HashType DefaultHashType = HashType.Vso0;
        private const string LocalHost = "localhost";
        private readonly Context _context;
        private GrpcCopyClientCache _clientCache;

        public GrpcCopyContentTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
            _context = new Context(Logger);
            _clientCache = new GrpcCopyClientCache(_context, maxClientCount: 65536);
        }

        [Fact]
        public async Task DuplicateClientsAreTheSameObject()
        {
            using (var client1 = await _clientCache.CreateAsync(LocalHost, 10, true))
            using (var client2 = await _clientCache.CreateAsync(LocalHost, 10, true))
            {
                Assert.Same(client1.Value, client2.Value);
            }
        }

        [Fact]
        public async Task ValidateBackgroundCleanup()
        {
            List<GrpcCopyClient> clientList = new List<GrpcCopyClient>();
            var key = new GrpcCopyClientKey(LocalHost, 11, true);
            ResourceWrapper<GrpcCopyClient> clientWrapper;
            using (clientWrapper = await _clientCache.CreateAsync(key.Host, key.GrpcPort, key.UseCompression))
            {
                clientList.Add(clientWrapper.Value);
                clientWrapper._lastUseTime = DateTime.UtcNow - TimeSpan.FromHours(2);
            }

            // Start cleanup now; don't wait for another loop
            await _clientCache.CleanupAsync(force: false, numberToRelease: int.MaxValue);

            var newClient = await _clientCache.CreateAsync(key.Host, key.GrpcPort, key.UseCompression);
            clientList.Add(newClient.Value);

            // If we found a different client, then cleanup successfully removed the original client
            Assert.NotSame(newClient.Value, clientWrapper.Value);
            Assert.Equal(1, newClient.Uses);
        }

        [Fact]
        public async Task ValidateSingleCleanup()
        {
            var clientsToCreate = 10;

            var clients = new List<GrpcCopyClient>();
            ResourceWrapper<GrpcCopyClient> clientWrapper;
            foreach (var num in Enumerable.Range(1, clientsToCreate))
            {
                var key = new GrpcCopyClientKey(LocalHost, num, true);

                using (clientWrapper = await _clientCache.CreateAsync(key.Host, key.GrpcPort, key.UseCompression))
                {
                    clients.Add(clientWrapper.Value);
                    clientWrapper._lastUseTime = DateTime.UtcNow - TimeSpan.FromHours(num); // Last client will be oldest.
                }
            }

            Assert.Equal(0, _clientCache.Counter[ResourcePoolCounters.Cleaned].Value);
            Assert.Equal(clientsToCreate, _clientCache.Counter[ResourcePoolCounters.Created].Value);

            await _clientCache.CleanupAsync(force: true, numberToRelease: 1);

            Assert.Equal(1, _clientCache.Counter[ResourcePoolCounters.Cleaned].Value);

            foreach (var client in clients.Take(clientsToCreate - 1))
            {
                Assert.False(client.ShutdownStarted);
            }

            Assert.True(clients.Last().ShutdownCompleted);
        }

        [Fact]
        public async Task IssueSameClientManyTimes()
        {
            using (ResourceWrapper<GrpcCopyClient> originalClientWrapper = await _clientCache.CreateAsync(LocalHost, 42, false))
            {
                var originalClient = originalClientWrapper.Value;

                for (int i = 0; i < 1000; i++)
                {
                    using (var newClient = await _clientCache.CreateAsync(LocalHost, 42, false))
                    {
                        Assert.Same(newClient.Value, originalClient);
                    }
                }
            }
        }

        [Fact]
        public async Task FillCacheWithoutRemovingClients()
        {
            int maxClientCount = 10;
            var clientWrapperList = new List<(ResourceWrapper<GrpcCopyClient> Wrapper, GrpcCopyClient Client)>();
            _clientCache = new GrpcCopyClientCache(_context, maxClientCount: maxClientCount, maxClientAgeMinutes: 63, waitBetweenCleanupMinutes: 30, bufferSize: 65536);

            for (int i = 0; i < maxClientCount; i++)
            {
                var clientWrapper = await _clientCache.CreateAsync(LocalHost, i, true);
                clientWrapperList.Add((clientWrapper, clientWrapper.Value));
            }

            // Create new clients for every port
            Assert.Equal(maxClientCount, _clientCache.Counter.GetCounterValue(ResourcePoolCounters.Created));

            // Zero clients were cleaned
            Assert.Equal(0, _clientCache.Counter.GetCounterValue(ResourcePoolCounters.Cleaned));

            // Zero clients were reused
            Assert.Equal(0, _clientCache.Counter.GetCounterValue(ResourcePoolCounters.Reused));

            foreach (var c in clientWrapperList)
            {
                c.Wrapper._lastUseTime -= TimeSpan.FromDays(1);
                c.Wrapper.Dispose();
            }

            await _clientCache.CleanupAsync(force: false, numberToRelease: int.MaxValue);

            // All clients were cleaned
            Assert.Equal(maxClientCount, _clientCache.Counter.GetCounterValue(ResourcePoolCounters.Cleaned));
        }

        [Fact]
        public async Task CopyExistingFile()
        {
            await RunTestCase(nameof(CopyExistingFile), async (rootPath, session, client) =>
            {
                // Write a random file
                var sourcePath = rootPath / ThreadSafeRandom.Generator.Next().ToString();
                var content = ThreadSafeRandom.GetBytes(FileSize);
                FileSystem.WriteAllBytes(sourcePath, content);

                // Put the random file
                PutResult putResult = await session.PutFileAsync(_context, HashType.Vso0, sourcePath, FileRealizationMode.Any, CancellationToken.None);
                putResult.ShouldBeSuccess();

                // Copy the file out via GRPC
                var destinationPath = rootPath / ThreadSafeRandom.Generator.Next().ToString();
                (await client.CopyFileAsync(_context, putResult.ContentHash, destinationPath, CancellationToken.None)).ShouldBeSuccess();

                var copied = FileSystem.ReadAllBytes(destinationPath);

                // Compare original and copied files
                var originalHash = content.CalculateHash(DefaultHashType);
                var copiedHash = copied.CalculateHash(DefaultHashType);
                Assert.Equal(originalHash, copiedHash);
            });
        }

        [Fact]
        public async Task CopyToShouldNotCloseGivenStream()
        {
            await RunTestCase(nameof(CopyExistingFile), async (rootPath, session, client) =>
            {
                // Write a random file
                var sourcePath = rootPath / ThreadSafeRandom.Generator.Next().ToString();
                var content = ThreadSafeRandom.GetBytes(FileSize);
                FileSystem.WriteAllBytes(sourcePath, content);

                // Put the random file
                PutResult putResult = await session.PutFileAsync(_context, HashType.Vso0, sourcePath, FileRealizationMode.Any, CancellationToken.None);
                putResult.ShouldBeSuccess();

                // Copy the file out via GRPC
                var destinationPath = rootPath / ThreadSafeRandom.Generator.Next().ToString();
                using (var destinationStream = await FileSystem.OpenAsync(
                    destinationPath,
                    FileAccess.ReadWrite,
                    FileMode.CreateNew,
                    FileShare.None,
                    FileOptions.None,
                    1024))
                {
                    (await client.CopyToAsync(_context, putResult.ContentHash, destinationStream, CancellationToken.None)).ShouldBeSuccess();
                    // If the stream is not disposed, the following operation should not fail.
                    destinationStream.Position.Should().BeGreaterThan(0);
                }
                
                var copied = FileSystem.ReadAllBytes(destinationPath);

                // Compare original and copied files
                var originalHash = content.CalculateHash(DefaultHashType);
                var copiedHash = copied.CalculateHash(DefaultHashType);
                Assert.Equal(originalHash, copiedHash);
            });
        }

        [Fact]
        public async Task CheckExistingFile()
        {
            await RunTestCase(nameof(CheckExistingFile), async (rootPath, session, client) =>
            {
                // Write a random file
                var sourcePath = rootPath / ThreadSafeRandom.Generator.Next().ToString();
                var content = ThreadSafeRandom.GetBytes(FileSize);
                FileSystem.WriteAllBytes(sourcePath, content);

                // Put the random file
                PutResult putResult = await session.PutFileAsync(_context, HashType.Vso0, sourcePath, FileRealizationMode.Any, CancellationToken.None);
                putResult.ShouldBeSuccess();

                // Check if file exists
                (await client.CheckFileExistsAsync(_context, putResult.ContentHash)).ShouldBeSuccess();
            });
        }

        [Fact]
        public async Task CopyNonExistingFile()
        {
            await RunTestCase(nameof(CopyNonExistingFile), async (rootPath, session, client) =>
            {
                // Copy the file out via GRPC
                var copyFileResult = await client.CopyFileAsync(_context, ContentHash.Random(), rootPath / ThreadSafeRandom.Generator.Next().ToString(), CancellationToken.None);

                Assert.False(copyFileResult.Succeeded);
                Assert.Equal(CopyFileResult.ResultCode.FileNotFoundError, copyFileResult.Code);
            });
        }

        [Fact]
        public async Task CheckNonExistingFile()
        {
            await RunTestCase(nameof(CheckNonExistingFile), async (rootPath, session, client) =>
            {
                // Check if random non-existent file exists
                (await client.CheckFileExistsAsync(_context, ContentHash.Random())).ShouldBeError();
            });
        }

        [Fact]
        public async Task WrongPort()
        {
            await RunTestCase(nameof(WrongPort), async (rootPath, session, client) =>
            {
                // Copy fake file out via GRPC
                var bogusPort = PortExtensions.GetNextAvailablePort();
                using (var clientWrapper = await _clientCache.CreateAsync(LocalHost, bogusPort, true))
                {
                    // Replace the given client with a bogus one
                    client = clientWrapper.Value;

                    var copyFileResult = await client.CopyFileAsync(_context, ContentHash.Random(), rootPath / ThreadSafeRandom.Generator.Next().ToString(), CancellationToken.None);
                    Assert.Equal(CopyFileResult.ResultCode.SourcePathError, copyFileResult.Code);
                }
            });
        }

        private async Task RunTestCase(string testName, Func<AbsolutePath, IContentSession, GrpcCopyClient, Task> testAct)
        {
            var cacheName = testName + "_cache";

            using (var directory = new DisposableDirectory(FileSystem))
            {
                var rootPath = directory.Path;

                var namedCacheRoots = new Dictionary<string, AbsolutePath> { { cacheName, rootPath } };
                var grpcPort = PortExtensions.GetNextAvailablePort();
                var grpcPortFileName = Guid.NewGuid().ToString();
                var configuration = new ServiceConfiguration(
                    namedCacheRoots,
                    rootPath,
                    42,
                    ServiceConfiguration.DefaultGracefulShutdownSeconds,
                    grpcPort,
                    grpcPortFileName);

                var storeConfig = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1);
                Func<AbsolutePath, IContentStore> contentStoreFactory = (path) =>
                    new FileSystemContentStore(
                        FileSystem,
                        SystemClock.Instance,
                        directory.Path,
                        new ConfigurationModel(storeConfig));

                var server = new LocalContentServer(FileSystem, Logger, testName, contentStoreFactory, new LocalServerConfiguration(configuration));

                await server.StartupAsync(_context).ShouldBeSuccess();
                var createSessionResult = await server.CreateSessionAsync(new OperationContext(_context), testName, cacheName, ImplicitPin.PutAndGet, Capabilities.ContentOnly);
                createSessionResult.ShouldBeSuccess();

                (int sessionId, AbsolutePath tempDir) = createSessionResult.Value;
                var session = server.GetSession(sessionId);

                // Create a GRPC client to connect to the server
                var port = new MemoryMappedFilePortReader(grpcPortFileName, Logger).ReadPort();
                using (var clientWrapper = await _clientCache.CreateAsync(LocalHost, port, false))
                {
                    // Run validation
                    await testAct(rootPath, session, clientWrapper.Value);
                }

                await server.ShutdownAsync(_context).ShouldBeSuccess();
            }
        }

        protected override void Dispose(bool disposing)
        {
            _clientCache.Dispose();

            base.Dispose(disposing);
        }
    }
}
