// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Extensions;
using ContentStoreTest.Stores;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

#pragma warning disable IDE0040 // Accessibility modifiers required

namespace ContentStoreTest.Sessions
{
    public abstract class LargeStreamServiceClientTests : TestBase
    {
        protected const string CacheName = "test";
        protected const uint MaxConnections = 4;
        protected const uint GracefulShutdownSeconds = ServiceConfiguration.DefaultGracefulShutdownSeconds;
        private const string Name = "name";
        private const HashType ContentHashType = HashType.Vso0;
        private const long DefaultMaxSize = 1 * 1024 * 1024;
        private static readonly CancellationToken Token = CancellationToken.None;
        protected readonly string Scenario;
        private long _maxSize = DefaultMaxSize;

        protected LargeStreamServiceClientTests(string scenario)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
            Scenario = scenario + ScenarioSuffix;
        }

        [Theory]
        [InlineData(true, "1GB")]
        [InlineData(false, "1GB")]
        [InlineData(true, "2GB")]
        [InlineData(false, "2GB")]
        public async Task LargeStreamRoundtrip(bool provideHash, string sizeExpression)
        {
            _maxSize = "5GB".ToSize();

            using (var directory = new DisposableDirectory(FileSystem))
            {
                var path1 = directory.CreateRandomFileName();
                var path2 = directory.CreateRandomFileName();

                using (var fileStream = new FileStream(path1.Path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fileStream.SetLength(sizeExpression.ToSize());
                }

                var contentHash1 = await FileSystem.CalculateHashAsync(path1, ContentHashType);

                using (var fileStream1 = File.OpenRead(path1.Path))
                {
                    await RunTestAsync(ImplicitPin.None, directory, async (context, session) =>
                    {
                        // Stream content into the cache.
                        PutResult r1 = provideHash
                            ? await session.PutStreamAsync(context, contentHash1, fileStream1, Token)
                            : await session.PutStreamAsync(context, ContentHashType, fileStream1, Token);
                        r1.ShouldBeSuccess();

                        // Stream content back from the cache to a file.
                        var r2 = await session.OpenStreamAsync(context, contentHash1, Token).ShouldBeSuccess();

                        using (r2.Stream)
                        using (var fileStream2 = new FileStream(path2.Path, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await r2.Stream.CopyToAsync(fileStream2);
                        }

                        // Verify received content is the same.
                        var contentHash2 = await FileSystem.CalculateHashAsync(path2, ContentHashType);
                        contentHash2.Should().Be(contentHash1);
                    });
                }
            }
        }

        private static async Task RunTestImplAsync(
            Context context, IContentStore store, ImplicitPin implicitPin, Func<Context, IContentSession, Task> funcAsync)
        {
            var createResult = store.CreateSession(context, Name, implicitPin).ShouldBeSuccess();
            using (var session = createResult.Session)
            {
                try
                {
                    await session.StartupAsync(context).ShouldBeSuccess();
                    await funcAsync(context, session);
                }
                finally
                {
                    await session.ShutdownAsync(context).ShouldBeSuccess();
                }
            }
        }

        private async Task RunTestAsync(
            ImplicitPin implicitPin, DisposableDirectory directory, Func<Context, IContentSession, Task> funcAsync)
        {
            var context = new Context(Logger);
            var config = new ContentStoreConfiguration(new MaxSizeQuota($"{_maxSize}"));

            using (var store = CreateStore(directory.Path, config))
            {
                try
                {
                    await store.StartupAsync(context).ShouldBeSuccess();

                    await RunTestImplAsync(context, store, implicitPin, funcAsync);
                }
                finally
                {
                    await store.ShutdownAsync(context).ShouldBeSuccess();
                }
            }
        }

        protected abstract IContentStore CreateStore(AbsolutePath rootPath, ContentStoreConfiguration configuration);
    }

    [Trait("Category", "Integration")]
    [Trait("Category", "Integration1")]
    [Trait("Category", "QTestSkip")]
    /*public*/ class InProcessLargeStreamServiceClientTests : LargeStreamServiceClientTests
    {
       public InProcessLargeStreamServiceClientTests()
           : this(nameof(InProcessLargeStreamServiceClientTests))
       {
       }

       protected InProcessLargeStreamServiceClientTests(string scenarioName)
           : base(scenarioName)
       {
       }

       protected override IContentStore CreateStore(AbsolutePath rootPath, ContentStoreConfiguration configuration)
       {
           configuration.Write(FileSystem, rootPath).Wait();
           var grpcPortFileName = Guid.NewGuid().ToString();

           var serviceConfiguration = new ServiceConfiguration(
               new Dictionary<string, AbsolutePath> { { CacheName, rootPath } },
               rootPath,
               MaxConnections,
               GracefulShutdownSeconds,
               PortExtensions.GetNextAvailablePort(),
               grpcPortFileName);

           return new TestInProcessServiceClientContentStore(
               FileSystem, Logger, CacheName, Scenario, null, serviceConfiguration);
       }
    }

    [Trait("Category", "Integration")]
    [Trait("Category", "Integration1")]
    [Trait("Category", "QTestSkip")]
    /*public*/ class ExternalProcessLargeStreamServiceClientTests : LargeStreamServiceClientTests
    {
        public ExternalProcessLargeStreamServiceClientTests()
            : this(nameof(ExternalProcessLargeStreamServiceClientTests))
        {
        }

        protected ExternalProcessLargeStreamServiceClientTests(string scenarioName)
            : base(scenarioName)
        {
        }

        protected override IContentStore CreateStore(AbsolutePath rootPath, ContentStoreConfiguration configuration)
        {
            configuration.Write(FileSystem, rootPath).Wait();
            var grpcPortFileName = Guid.NewGuid().ToString();

            var serviceConfiguration = new ServiceConfiguration(
                new Dictionary<string, AbsolutePath> {{CacheName, rootPath}},
                rootPath,
                MaxConnections,
                GracefulShutdownSeconds,
                PortExtensions.GetNextAvailablePort(),
                grpcPortFileName);

            return new TestServiceClientContentStore(
                Logger,
                FileSystem,
                new ServiceClientContentStoreConfiguration(CacheName, null, Scenario), 
                null,
                serviceConfiguration);
        }
    }
}
