// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.CompilerServices;
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
using BuildXL.Utilities.Tasks;
using ContentStoreTest.Extensions;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Stores
{
    public class GrpcCopyContentTests : TestBase
    {
        private const int FileSize = 1000;
        private const HashType DefaultHashType = HashType.Vso0;
        private const string LocalHost = "localhost";
        private readonly Context _context;
        private readonly GrpcCopyClientCache _clientCache;

        public GrpcCopyContentTests(ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, output)
        {
            _context = new Context(Logger);
            _clientCache = new GrpcCopyClientCache(_context, new GrpcCopyClientCacheConfiguration()
            {
                ResourcePoolConfiguration = new BuildXL.Cache.ContentStore.Utils.ResourcePoolConfiguration()
                {
                    MaximumResourceCount = 1024,
                },
            });
        }

        [Fact]
        public async Task CopyExistingFile()
        {
            await RunTestCase(async (rootPath, session, client) =>
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
                (await client.CopyFileAsync(new OperationContext(_context), putResult.ContentHash, destinationPath, options: null)).ShouldBeSuccess();

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
            await RunTestCase(async (rootPath, session, client) =>
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
                using (var destinationStream = await FileSystem.OpenSafeAsync(
                    destinationPath,
                    FileAccess.ReadWrite,
                    FileMode.CreateNew,
                    FileShare.None,
                    FileOptions.None,
                    1024))
                {
                    (await client.CopyToAsync(new OperationContext(_context), putResult.ContentHash, destinationStream, options: null)).ShouldBeSuccess();
                    // If the stream is not disposed, the following operation should not fail.
                    destinationStream.Stream.Position.Should().BeGreaterThan(0);
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
            await RunTestCase(async (rootPath, session, client) =>
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
            await RunTestCase(async (rootPath, session, client) =>
            {
                // Copy the file out via GRPC
                var copyFileResult = await client.CopyFileAsync(new OperationContext(_context), ContentHash.Random(), rootPath / ThreadSafeRandom.Generator.Next().ToString(), options: null);

                Assert.False(copyFileResult.Succeeded);
                Assert.Equal(CopyResultCode.FileNotFoundError, copyFileResult.Code);
            });
        }

        [Fact]
        public async Task CheckNonExistingFile()
        {
            await RunTestCase(async (rootPath, session, client) =>
            {
                // Check if random non-existent file exists
                (await client.CheckFileExistsAsync(_context, ContentHash.Random())).ShouldBeError();
            });
        }

        [Fact]
        public async Task WrongPort()
        {
            await RunTestCase(async (rootPath, session, client) =>
            {
                // Copy fake file out via GRPC
                var bogusPort = PortExtensions.GetNextAvailablePort();

                await _clientCache.UseAsync(new OperationContext(_context), LocalHost, bogusPort, async (nestedContext, client) =>
                {
                    var copyFileResult = await client.CopyFileAsync(nestedContext, ContentHash.Random(), rootPath / ThreadSafeRandom.Generator.Next().ToString(), options: null);
                    Assert.Equal(CopyResultCode.ServerUnavailable, copyFileResult.Code);
                    return Unit.Void;
                });
            });
        }

        private async Task RunTestCase(Func<AbsolutePath, IContentSession, GrpcCopyClient, Task> testAct, [CallerMemberName] string testName = null)
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
                await _clientCache.UseAsync(new OperationContext(_context), LocalHost, port, async (nestedContext, grpcCopyClient) =>
                {
                    await testAct(rootPath, session, grpcCopyClient);
                    return Unit.Void;
                });

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
