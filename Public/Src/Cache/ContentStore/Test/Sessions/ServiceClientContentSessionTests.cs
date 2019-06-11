// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Sessions;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using ContentStoreTest.Extensions;
using ContentStoreTest.Stores;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Sessions
{
    public abstract class ServiceClientContentSessionTests : ContentSessionTests
    {
        protected const string CacheName = "test";
        protected const uint MaxConnections = 4;
        protected const uint ConnectionsPerSession = 2;
        private const uint RetryIntervalSeconds = ServiceClientContentStore.DefaultRetryIntervalSeconds;
        private const uint RetryCount = ServiceClientContentStore.DefaultRetryCount;
        protected const uint GracefulShutdownSeconds = ServiceConfiguration.DefaultGracefulShutdownSeconds;
        private const int RandomContentByteCount = 100;
        protected readonly string Scenario;
        private readonly Context _context;

        protected ServiceClientContentSessionTests(string scenario)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, false)
        {
            Contract.Requires(scenario != null);

            Scenario = scenario + TestBase.ScenarioSuffix;
            _context = new Context(Logger);
        }

        protected override long MaxSize { get; } = DefaultMaxSize;

        protected abstract IStartupShutdown CreateServer(ServiceConfiguration serviceConfiguration);

        [Fact]
        public async Task ConnectToInvalidCacheGivesError()
        {
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var config = CreateStoreConfiguration();

                using (var store = CreateStore(directory, config))
                {
                    ((ITestServiceClientContentStore)store).SetOverrideCacheName("invalid");

                    try
                    {
                        await store.StartupAsync(_context).ShouldBeSuccess();

                        var createResult = store.CreateSession(_context, Name, ImplicitPin.None);
                        using (var session = createResult.Session)
                        {
                            var r = await session.StartupAsync(_context);
                            r.ErrorMessage.Should().Contain("is not available");
                        }
                    }
                    finally
                    {
                        await store.ShutdownAsync(_context).ShouldBeSuccess();
                    }
                }
            }
        }

        [Fact]
        public async Task MultipleClients()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var rootPath = testDirectory.Path;
                var config = CreateStoreConfiguration();
                await config.Write(FileSystem, rootPath);

                using (var store = CreateStore(testDirectory, config))
                {
                    try
                    {
                        var r = await store.StartupAsync(_context);
                        r.ShouldBeSuccess();

                        IContentSession session1 = null;
                        IContentSession session2 = null;

                        try
                        {
                            var createSessionResult1 = store.CreateSession(_context, "session1", ImplicitPin.None);
                            createSessionResult1.ShouldBeSuccess();

                            var createSessionResult2 = store.CreateSession(_context, "session2", ImplicitPin.None);
                            createSessionResult2.ShouldBeSuccess();

                            using (createSessionResult1.Session)
                            using (createSessionResult2.Session)
                            {
                                var r1 = await createSessionResult1.Session.StartupAsync(_context);
                                r1.ShouldBeSuccess();
                                session1 = createSessionResult1.Session;

                                var startupSessionResult2 = await createSessionResult2.Session.StartupAsync(_context);
                                startupSessionResult2.ShouldBeSuccess();
                                session2 = createSessionResult2.Session;

                                var putResult = await session1.PutRandomAsync(
                                    _context, ContentHashType, false, RandomContentByteCount, Token);

                                var tasks1 = Enumerable.Range(0, (int)ConnectionsPerSession)
                                    .Select(_ => Task.Run(async () =>
                                        await session1.PinAsync(_context, putResult.ContentHash, Token)))
                                    .ToList();

                                var tasks2 = Enumerable.Range(0, (int)ConnectionsPerSession)
                                    .Select(_ => Task.Run(async () =>
                                        await session2.PinAsync(_context, putResult.ContentHash, Token)))
                                    .ToList();

                                foreach (var task in tasks1.Union(tasks2))
                                {
                                    var result = await task;
                                    result.ShouldBeSuccess();
                                }
                            }
                        }
                        finally
                        {
                            if (session2 != null)
                            {
                                await session2.ShutdownAsync(_context).ShouldBeSuccess();
                            }

                            if (session1 != null)
                            {
                                await session1.ShutdownAsync(_context).ShouldBeSuccess();
                            }
                        }
                    }
                    finally
                    {
                        var r = await store.ShutdownAsync(_context);
                        r.ShouldBeSuccess();
                    }
                }
            }
        }

        [Fact]
        public async Task MultipleCaches()
        {
            const string cacheName1 = "test1";
            const string cacheName2 = "test2";
            using (var testDirectory0 = new DisposableDirectory(FileSystem))
            using (var testDirectory1 = new DisposableDirectory(FileSystem))
            using (var testDirectory2 = new DisposableDirectory(FileSystem))
            {
                var config = CreateStoreConfiguration();

                var rootPath1 = testDirectory1.Path;
                await config.Write(FileSystem, rootPath1);

                var rootPath2 = testDirectory2.Path;
                await config.Write(FileSystem, rootPath2);

                var grpcPort = PortExtensions.GetNextAvailablePort();
                var grpcPortFileName = Guid.NewGuid().ToString();

                var serviceConfiguration = new ServiceConfiguration(
                    new Dictionary<string, AbsolutePath> { { cacheName1, rootPath1 }, { cacheName2, rootPath2 } },
                    testDirectory0.Path,
                    ServiceConfiguration.DefaultMaxConnections,
                    ServiceConfiguration.DefaultGracefulShutdownSeconds,
                    grpcPort,
                    grpcPortFileName);

                using (var server = CreateServer(serviceConfiguration))
                {
                    var factory = new MemoryMappedFileGrpcPortSharingFactory(Logger, grpcPortFileName);
                    var reader = factory.GetPortReader();
                    var port = reader.ReadPort();
                    var rpcConfig = new ServiceClientRpcConfiguration(port);

                    using (var store1 = new ServiceClientContentStore(
                        Logger,
                        FileSystem,
                        new ServiceClientContentStoreConfiguration(cacheName1, rpcConfig, Scenario)))
                    using (var store2 = new ServiceClientContentStore(
                        Logger,
                        FileSystem,
                        new ServiceClientContentStoreConfiguration(cacheName2, rpcConfig, Scenario)))
                    {
                        try
                        {
                            var rs = await server.StartupAsync(_context);
                            rs.ShouldBeSuccess();

                            var storeBoolResult1 = await store1.StartupAsync(_context);
                            storeBoolResult1.ShouldBeSuccess();

                            var storeBoolResult2 = await store2.StartupAsync(_context);
                            storeBoolResult2.ShouldBeSuccess();

                            IContentSession session1 = null;
                            IContentSession session2 = null;

                            try
                            {
                                var createSessionResult1 = store1.CreateSession(_context, "session1", ImplicitPin.None);
                                createSessionResult1.ShouldBeSuccess();

                                var createSessionResult2 = store2.CreateSession(_context, "session2", ImplicitPin.None);
                                createSessionResult2.ShouldBeSuccess();

                                using (createSessionResult1.Session)
                                using (createSessionResult2.Session)
                                {
                                    var r1 = await createSessionResult1.Session.StartupAsync(_context);
                                    r1.ShouldBeSuccess();
                                    session1 = createSessionResult1.Session;

                                    var r2 = await createSessionResult2.Session.StartupAsync(_context);
                                    r2.ShouldBeSuccess();
                                    session2 = createSessionResult2.Session;

                                    var r3 = await session1.PutRandomAsync(
                                        _context,
                                        ContentHashType,
                                        false,
                                        RandomContentByteCount,
                                        Token);
                                    var pinResult = await session1.PinAsync(_context, r3.ContentHash, Token);
                                    pinResult.ShouldBeSuccess();

                                    r3 = await session2.PutRandomAsync(
                                        _context,
                                        ContentHashType,
                                        false,
                                        RandomContentByteCount,
                                        Token);
                                    pinResult = await session2.PinAsync(_context, r3.ContentHash, Token);
                                    pinResult.ShouldBeSuccess();
                                }
                            }
                            finally
                            {
                                if (session2 != null)
                                {
                                    await session2.ShutdownAsync(_context).ShouldBeSuccess();
                                }

                                if (session1 != null)
                                {
                                    await session1.ShutdownAsync(_context).ShouldBeSuccess();
                                }
                            }
                        }
                        finally
                        {
                            BoolResult r1 = null;
                            BoolResult r2 = null;

                            if (store1.StartupCompleted)
                            {
                                r1 = await store1.ShutdownAsync(_context);
                            }

                            if (store2.StartupCompleted)
                            {
                                r2 = await store2.ShutdownAsync(_context);
                            }

                            var r3 = await server.ShutdownAsync(_context);

                            r1?.ShouldBeSuccess();
                            r2?.ShouldBeSuccess();
                            r3?.ShouldBeSuccess();
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task DisposeAfterStartupFailure()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var config = CreateStoreConfiguration();

                using (var store = CreateStore(testDirectory, config))
                {
                    try
                    {
                        ((ITestServiceClientContentStore)store).SetDoNotStartService(true);
                        var r = await store.StartupAsync(_context);
                        r.ShouldBeSuccess();

                        var createResult = store.CreateSession(_context, Name, ImplicitPin.None);
                        createResult.ShouldBeSuccess();
                        using (var session = createResult.Session)
                        {
                            var r2 = await session.StartupAsync(_context);
                            r2.ShouldBeError();
                        }
                    }
                    finally
                    {
                        var r = await store.ShutdownAsync(_context);
                        r.ShouldBeSuccess();
                    }
                }
            }
        }

        internal class RestrictedMemoryStream : MemoryStream
        {
            public RestrictedMemoryStream(byte[] buffer) : base(buffer, false)
            {
            }

            public override long Length => throw new NotImplementedException();

            public override bool CanWrite => throw new NotImplementedException();

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override void WriteByte(byte value)
            {
                throw new NotImplementedException();
            }

            public override bool CanSeek => throw new NotImplementedException();

            public override long Seek(long offset, SeekOrigin loc)
            {
                throw new NotImplementedException();
            }
        }

        [Fact]
        public async Task PutStreamSucceedsWithNonSeekableStream()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var rootPath = testDirectory.Path;
                var config = CreateStoreConfiguration();
                await config.Write(FileSystem, rootPath);

                using (var store = CreateStore(testDirectory, config))
                {
                    try
                    {
                        var r = await store.StartupAsync(_context);
                        r.ShouldBeSuccess();

                        IContentSession session1 = null;

                        try
                        {
                            var createSessionResult1 = store.CreateSession(_context, "session1", ImplicitPin.None);
                            createSessionResult1.ShouldBeSuccess();

                            using (createSessionResult1.Session)
                            {
                                var r1 = await createSessionResult1.Session.StartupAsync(_context);
                                r1.ShouldBeSuccess();
                                session1 = createSessionResult1.Session;

                                using (var memoryStream = new RestrictedMemoryStream(ThreadSafeRandom.GetBytes(RandomContentByteCount)))
                                {
                                    var result = await session1.PutStreamAsync(_context, ContentHashType, memoryStream, Token);
                                    result.ShouldBeSuccess();
                                    result.ContentSize.Should().Be(RandomContentByteCount);
                                }
                            }
                        }
                        finally
                        {
                            if (session1 != null)
                            {
                                await session1.ShutdownAsync(_context).ShouldBeSuccess();
                            }
                        }
                    }
                    finally
                    {
                        var r = await store.ShutdownAsync(_context);
                        r.ShouldBeSuccess();
                    }
                }
            }
        }
    }
}
