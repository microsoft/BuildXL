// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Extensions;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Grpc
{
    // ReSharper disable once UnusedMember.Global
    [Trait("Category", "Integration")]
    [Trait("Category", "QTestSkip")]
    public class GrpcLocalContentServerShutdownTests : TestBase
    {
        private const string CacheName = "cacheName";
        private const string SessionName = "sessionName";
        private const uint MaxConnections = ServiceConfiguration.DefaultMaxConnections;

        public GrpcLocalContentServerShutdownTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task LongPinCanceledOnShutdown(bool respectsCancellationToken)
        {
            string scenario = nameof(LongPinCanceledOnShutdown) + respectsCancellationToken;
            return LongCallCanceledOnShutdownAsync(
                scenario,
                respectsCancellationToken,
                (context, testFilePath, rpcClient) => rpcClient.PinAsync(context, ContentHash.Random()));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task LongPinBulkCanceledOnShutdown(bool respectsCancellationToken)
        {
            string scenario = nameof(LongPinCanceledOnShutdown) + respectsCancellationToken;
            return LongCallCanceledOnShutdownAsync(
                scenario,
                respectsCancellationToken,
                (context, testFilePath, rpcClient) => rpcClient.PinAsync(context, new[] {ContentHash.Random()}));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task LongPutFileCanceledOnShutdown(bool respectsCancellationToken)
        {
            string scenario = nameof(LongPinCanceledOnShutdown) + respectsCancellationToken;
            return LongCallCanceledOnShutdownAsync(
                scenario,
                respectsCancellationToken,
                (context, testFilePath, rpcClient) =>
                    rpcClient.PutFileAsync(context, HashType.Vso0, testFilePath, FileRealizationMode.Any));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task LongPutStreamCanceledOnShutdown(bool respectsCancellationToken)
        {
            string scenario = nameof(LongPinCanceledOnShutdown) + respectsCancellationToken;
            return LongCallCanceledOnShutdownAsync(
                scenario,
                respectsCancellationToken,
                async (context, testFilePath, rpcClient) =>
                {
                    using (var stream = new MemoryStream())
                    {
                        return await rpcClient.PutStreamAsync(context, HashType.Vso0, stream);
                    }
                });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task LongOpenStreamCanceledOnShutdown(bool respectsCancellationToken)
        {
            string scenario = nameof(LongPinCanceledOnShutdown) + respectsCancellationToken;
            return LongCallCanceledOnShutdownAsync(
                scenario,
                respectsCancellationToken,
                (context, testFilePath, rpcClient) =>
                    rpcClient.OpenStreamAsync(context, ContentHash.Random()));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task LongPlaceFileCanceledOnShutdown(bool respectsCancellationToken)
        {
            string scenario = nameof(LongPinCanceledOnShutdown) + respectsCancellationToken;
            return LongCallCanceledOnShutdownAsync(
                scenario,
                respectsCancellationToken,
                (context, testFilePath, rpcClient) =>
                    rpcClient.PlaceFileAsync(
                        context,
                        ContentHash.Random(),
                        testFilePath,
                        FileAccessMode.Write,
                        FileReplacementMode.FailIfExists,
                        FileRealizationMode.Any));
        }

        private async Task LongCallCanceledOnShutdownAsync<T>(
            string scenario, bool respectsCancellationToken, Func<Context, AbsolutePath, IRpcClient, Task<T>> unresponsiveFunc)
        {
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var rootPath = directory.Path;

                var namedCacheRoots = new Dictionary<string, AbsolutePath> { { CacheName, rootPath } };
                var grpcPort = PortExtensions.GetNextAvailablePort();
                var grpcPortFileName = Guid.NewGuid().ToString();
                var configuration = new ServiceConfiguration(
                    namedCacheRoots,
                    rootPath,
                    MaxConnections,
                    ServiceConfiguration.DefaultGracefulShutdownSeconds,
                    grpcPort,
                    grpcPortFileName);

                // Use a store which gets stuck forever on the session operations (pin/place/put/open) unless canceled,
                // and configurably ignoring the cancellation token to simulate long operations that might not check
                // the token quickly enough.
                var unresponsivenessHasStartedSemaphore = new SemaphoreSlim(0, 1);
                Func<AbsolutePath, IContentStore> contentStoreFactory =
                    path => new TestHangingContentStore(respectsCancellationToken, unresponsivenessHasStartedSemaphore);

                IRpcClient rpcClient;
                Task<T> unresponsiveTask;
                using (var server = new LocalContentServer(FileSystem, Logger, scenario, contentStoreFactory, new LocalServerConfiguration(configuration)))
                {
                    var tracer = new ServiceClientContentSessionTracer("TestTracerForRpcClient");
                    await server.StartupAsync(context).ShouldBeSuccess();

                    var port = new MemoryMappedFilePortReader(grpcPortFileName, Logger).ReadPort();
                    rpcClient = new GrpcContentClient(tracer, FileSystem, port, scenario);

                    await rpcClient.CreateSessionAsync(
                        context, SessionName, CacheName, ImplicitPin.None).ShouldBeSuccess();

                    // Start the task which we expect to become unresponsive
                    unresponsiveTask = unresponsiveFunc(context, directory.CreateRandomFileName(), rpcClient);

                    // Synchronize with the store's unresponsiveness.
                    await unresponsivenessHasStartedSemaphore.WaitAsync();

                     await server.ShutdownAsync(context).ShouldBeSuccess();
                }

                // Make sure that the client gets a retryable exception
                Func<Task> awaitUnresponsiveTaskFunc = async () => await unresponsiveTask;
                awaitUnresponsiveTaskFunc.Should().Throw<ClientCanRetryException>();

                (await rpcClient.ShutdownAsync(context)).ShouldBeSuccess();
                rpcClient.Dispose();
            }
        }

        /// <summary>
        ///     Because service shutdowns can truncate PlaceFile calls,
        ///     and because we can't determine whether or not a call is a retry,
        ///     we can't provide these checks on the service side.
        /// </summary>
        [Theory]
        [InlineData(FileReplacementMode.ReplaceExisting)]
        [InlineData(FileReplacementMode.FailIfExists)]
        [InlineData(FileReplacementMode.SkipIfExists)]
        public async Task DoesNotRespectFileReplacementMode(FileReplacementMode requestedReplacementMode)
        {
            string scenario = nameof(DoesNotRespectFileReplacementMode) + requestedReplacementMode;
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var rootPath = directory.Path;

                var namedCacheRoots = new Dictionary<string, AbsolutePath> { { CacheName, rootPath } };
                var grpcPort = PortExtensions.GetNextAvailablePort();
                var grpcPortFileName = Guid.NewGuid().ToString();
                var configuration = new ServiceConfiguration(
                    namedCacheRoots,
                    rootPath,
                    MaxConnections,
                    ServiceConfiguration.DefaultGracefulShutdownSeconds,
                    grpcPort,
                    grpcPortFileName);
                Func<AbsolutePath, IContentStore> contentStoreFactory =
                    path =>
                        new FileSystemContentStore(
                            FileSystem,
                            SystemClock.Instance,
                            rootPath,
                            new ConfigurationModel(new ContentStoreConfiguration(new MaxSizeQuota("1MB"))));

                using (var server = new LocalContentServer(FileSystem, Logger, scenario, contentStoreFactory, new LocalServerConfiguration(configuration)))
                {
                    var tracer = new ServiceClientContentSessionTracer("TestTracerForRpcClient");
                    await server.StartupAsync(context).ShouldBeSuccess();

                    var port = new MemoryMappedFilePortReader(grpcPortFileName, Logger).ReadPort();
                    IRpcClient rpcClient = new GrpcContentClient(tracer, FileSystem, port, scenario);

                    await rpcClient.CreateSessionAsync(
                        context, SessionName, CacheName, ImplicitPin.None).ShouldBeSuccess();

                    ContentHash contentHash;
                    using (var stream = new MemoryStream())
                    {
                        PutResult putResult = await rpcClient.PutStreamAsync(context, HashType.Vso0, stream);
                        putResult.ShouldBeSuccess();
                        putResult.ContentSize.Should().Be(0);
                        contentHash = putResult.ContentHash;
                    }

                    var tempPath = directory.CreateRandomFileName();
                    FileSystem.WriteAllBytes(tempPath, new byte[] {});

                    var placeResult = await rpcClient.PlaceFileAsync(
                        context, contentHash, tempPath, FileAccessMode.ReadOnly, requestedReplacementMode, FileRealizationMode.Any);
                    placeResult.Succeeded.Should().BeTrue();

                    (await rpcClient.ShutdownAsync(context)).ShouldBeSuccess();
                    rpcClient.Dispose();

                    await server.ShutdownAsync(context).ShouldBeSuccess();
                }
            }
        }

        [Fact]
        public async Task CreateSessionThrowsClientCanRetryExceptionWhenServiceOffline()
        {
            string scenario = nameof(CreateSessionThrowsClientCanRetryExceptionWhenServiceOffline);
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var rootPath = directory.Path;

                var namedCacheRoots = new Dictionary<string, AbsolutePath> { { CacheName, rootPath } };
                var grpcPort = PortExtensions.GetNextAvailablePort();
                var grpcPortFileName = Guid.NewGuid().ToString();
                var configuration = new ServiceConfiguration(
                    namedCacheRoots,
                    rootPath,
                    MaxConnections,
                    ServiceConfiguration.DefaultGracefulShutdownSeconds,
                    grpcPort,
                    grpcPortFileName);
                Func<AbsolutePath, IContentStore> contentStoreFactory =
                    path =>
                        new FileSystemContentStore(
                            FileSystem,
                            SystemClock.Instance,
                            rootPath,
                            new ConfigurationModel(new ContentStoreConfiguration(new MaxSizeQuota("1KB"))));

                IRpcClient rpcClient;
                using (var server = new LocalContentServer(FileSystem, Logger, scenario, contentStoreFactory, new LocalServerConfiguration(configuration)))
                {
                    var tracer = new ServiceClientContentSessionTracer("TestTracerForRpcClient");
                    BoolResult r = await server.StartupAsync(context).ConfigureAwait(false);
                    r.ShouldBeSuccess();

                    var port = new MemoryMappedFilePortReader(grpcPortFileName, Logger).ReadPort();
                    rpcClient = new GrpcContentClient(tracer, FileSystem, port, scenario);

                    r = await server.ShutdownAsync(context);
                    r.ShouldBeSuccess();
                }

                Func<Task<BoolResult>> createSessionFunc = () => rpcClient.CreateSessionAsync(context, SessionName, CacheName, ImplicitPin.None);
                createSessionFunc.Should().Throw<ClientCanRetryException>();

                rpcClient.Dispose();
            }
        }

        [Fact]
        public async Task PutStreamRetriesWhenTempFileDisappears()
        {
            string scenario = nameof(PutStreamRetriesWhenTempFileDisappears);
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var rootPath = directory.Path;

                var namedCacheRoots = new Dictionary<string, AbsolutePath> { { CacheName, rootPath } };
                var grpcPort = PortExtensions.GetNextAvailablePort();
                var grpcPortFileName = Guid.NewGuid().ToString();
                var configuration = new ServiceConfiguration(
                    namedCacheRoots,
                    rootPath,
                    MaxConnections,
                    ServiceConfiguration.DefaultGracefulShutdownSeconds,
                    grpcPort,
                    grpcPortFileName);
                Func<AbsolutePath, IContentStore> contentStoreFactory = path => new TestTempFileDeletingContentStore(FileSystem);

                using (var server = new LocalContentServer(FileSystem, Logger, scenario, contentStoreFactory, new LocalServerConfiguration(configuration)))
                {
                    var tracer = new ServiceClientContentSessionTracer("TestTracerForRpcClient");
                    await server.StartupAsync(context).ShouldBeSuccess();

                    var port = new MemoryMappedFilePortReader(grpcPortFileName, Logger).ReadPort();
                    using (IRpcClient rpcClient = new GrpcContentClient(tracer, FileSystem, port, scenario))
                    {
                        await rpcClient.CreateSessionAsync(
                            context, SessionName, CacheName, ImplicitPin.None).ShouldBeSuccess();

                        using (var stream = new MemoryStream())
                        {
                            Func<Task<PutResult>> putFunc = () => rpcClient.PutStreamAsync(context, HashType.Vso0, stream);
                            putFunc.Should().Throw<ClientCanRetryException>();
                        }

                        (await rpcClient.ShutdownAsync(context)).ShouldBeSuccess();
                    }

                    await server.ShutdownAsync(context).ShouldBeSuccess();
                }
            }
        }

        [Fact]
        public async Task OpenStreamRetriesWhenTempFileDisappears()
        {
            string scenario = nameof(PutStreamRetriesWhenTempFileDisappears);
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var rootPath = directory.Path;

                var namedCacheRoots = new Dictionary<string, AbsolutePath> { { CacheName, rootPath } };
                var grpcPort = PortExtensions.GetNextAvailablePort();
                var grpcPortFileName = Guid.NewGuid().ToString();
                var configuration = new ServiceConfiguration(
                    namedCacheRoots,
                    rootPath,
                    MaxConnections,
                    ServiceConfiguration.DefaultGracefulShutdownSeconds,
                    grpcPort,
                    grpcPortFileName);
                Func<AbsolutePath,  IContentStore> contentStoreFactory = path => new TestTempFileDeletingContentStore(FileSystem);

                using (var server = new LocalContentServer(FileSystem, Logger, scenario, contentStoreFactory, new LocalServerConfiguration(configuration)))
                {
                    var tracer = new ServiceClientContentSessionTracer("TestTracerForRpcClient");
                    await server.StartupAsync(context).ShouldBeSuccess();

                    var port = new MemoryMappedFilePortReader(grpcPortFileName, Logger).ReadPort();
                    using (IRpcClient rpcClient = new GrpcContentClient(tracer, FileSystem, port, scenario))
                    {
                        await rpcClient.CreateSessionAsync(
                            context, SessionName, CacheName, ImplicitPin.None).ShouldBeSuccess();

                        Func<Task<OpenStreamResult>> openResult = () => rpcClient.OpenStreamAsync(context, ContentHash.Random());
                        openResult.Should().Throw<ClientCanRetryException>();

                        (await rpcClient.ShutdownAsync(context)).ShouldBeSuccess();
                    }

                    await server.ShutdownAsync(context).ShouldBeSuccess();
                }
            }
        }
    }
}
