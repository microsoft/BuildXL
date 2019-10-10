// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Extensions;
using ContentStoreTest.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable SA1402 // File may only contain a single class
#pragma warning disable IDE0040 // Accessibility modifiers required

namespace ContentStoreTest.Sessions
{
    public abstract class ServiceRequestsWorkBeforeAndAfterServerRestartTests<T> : ServiceClientContentSessionTestBase<T>
        where T : ServiceClientContentStore, ITestServiceClientContentStore
    {
        protected const uint RetrySeconds = 1;
        protected const uint RetryCount = 1;

        protected ServiceRequestsWorkBeforeAndAfterServerRestartTests(string scenario, ITestOutputHelper output = null)
            : base(scenario, output)
        {
        }
        
        [Fact]
        public Task PinWorksBeforeAndAfterServerRestart()
        {
            return WorksBeforeAndAfterServerRestart(async (context, session, contentHash) =>
            {
                 await session.PinAsync(context, contentHash, Token).ShouldBeSuccess();
            });
        }

        [Fact]
        public Task OpenStreamWorksBeforeAndAfterServerRestart()
        {
            return WorksBeforeAndAfterServerRestart(async (context, session, contentHash) =>
            {
                var r = await session.OpenStreamAsync(context, contentHash, Token);
                r.Stream.Should().NotBeNull();
                using (r.Stream)
                {
                    r.ShouldBeSuccess();
                }
            });
        }
        
        [Fact]
        public async Task PlaceFileWorksBeforeAndAfterServerRestart()
        {
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var path = directory.Path / "file.dat";
                await WorksBeforeAndAfterServerRestart(async (context, session, contentHash) =>
                {
                    var r = await session.PlaceFileAsync(
                        context,
                        contentHash,
                        path,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.ReplaceExisting,
                        FileRealizationMode.Any,
                        Token);
                    r.IsPlaced().Should().BeTrue();
                });
            }
        }
        
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task PutStreamWorksBeforeAndAfterServerRestart(bool provideHash)
        {
            return WorksBeforeAndAfterServerRestart(async (context, session, contentHash) =>
            {
                await session.PutRandomAsync(context, ContentHashType, provideHash, ContentByteCount, Token).ShouldBeSuccess();
            });
        }
        
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task PutFileWorksBeforeAndAfterServerRestart(bool provideHash)
        {
            return WorksBeforeAndAfterServerRestart(async (context, session, contentHash) =>
            {
                await session.PutRandomFileAsync(
                    context, FileSystem, ContentHashType, provideHash, ContentByteCount, Token).ShouldBeSuccess();
            });
        }
        
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task PutFileBlockedForInsensitiveSessionsBeforeAndAfterServerRestart(bool provideHash)
        {
            return WorksBeforeAndAfterServerRestart(
                async (context, session, contentHash) =>
                {
                    await session.PutRandomFileAsync(
                        context, FileSystem, ContentHashType, provideHash, MaxSize, Token).ShouldBeError();
                });
        }
        
        private Task WorksBeforeAndAfterServerRestart(Func<Context, IContentSession, ContentHash, Task> requestFunc)
        {
            int retryCount = 0;
            int maxRetryCount = 2;
            List<string> errorMessages = new List<string>();

            while (retryCount < maxRetryCount)
            {
                try
                {
                    return RunSessionTestAsync(ImplicitPin.None, async (context, session) =>
                    {
                        // Put some random content for requests that want to use it.
                        var r1 = await session.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                        // Pin the content - this should survive the server restart.
                         await session.PinAsync(context, r1.ContentHash, Token).ShouldBeSuccess();

                        // Make sure request works before restarting the server.
                        await requestFunc(context, session, r1.ContentHash);

                        ITestServiceClientContentStore store = ((TestServiceClientContentSession)session).Store;
                        await store.RestartServerAsync(context);

                        // Make sure request works after restarting the server.
                        await requestFunc(context, session, r1.ContentHash);
                    });
                }
                catch (Xunit.Sdk.XunitException e)
                {
                    errorMessages.Add(e.Message);
                    retryCount++;
                }
            }

            Assert.True(false, $"Failed after {retryCount} tries." + string.Join(Environment.NewLine, errorMessages));
            return null;
        }
    }

    [Trait("Category", "Integration")]
    [Trait("Category", "Integration2")]
    [Trait("Category", "WindowsOSOnly")] // These use named event handles, which are not supported in .NET core
    public class InProcessServiceRequestsWorkBeforeAndAfterServerRestartTests : ServiceRequestsWorkBeforeAndAfterServerRestartTests<TestInProcessServiceClientContentStore>
    {
        public InProcessServiceRequestsWorkBeforeAndAfterServerRestartTests(ITestOutputHelper output)
            : base(nameof(InProcessServiceRequestsWorkBeforeAndAfterServerRestartTests), output)
        {
        }

        protected InProcessServiceRequestsWorkBeforeAndAfterServerRestartTests(string scenario, ITestOutputHelper output)
            : base(scenario, output)
        {
        }

        protected override TestInProcessServiceClientContentStore CreateStore(
            AbsolutePath rootPath,
            ContentStoreConfiguration configuration,
            LocalServerConfiguration localContentServerConfiguration,
            TimeSpan? heartbeatOverride)
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

            return new TestInProcessServiceClientContentStore
                (
                FileSystem,
                Logger,
                CacheName,
                Scenario,
                null,
                serviceConfiguration,
                RetrySeconds,
                RetryCount,
                localContentServerConfiguration
                );
        }
    }

    [Trait("Category", "Integration")]
    [Trait("Category", "Integration2")]
    [Trait("Category", "QTestSkip")]
    /*public*/ class ExternalProcessServiceRequestsWorkBeforeAndAfterServerRestartTests : ServiceRequestsWorkBeforeAndAfterServerRestartTests<TestServiceClientContentStore>
    {
       public ExternalProcessServiceRequestsWorkBeforeAndAfterServerRestartTests()
           : base(nameof(ExternalProcessServiceRequestsWorkBeforeAndAfterServerRestartTests))
       {
       }

       protected ExternalProcessServiceRequestsWorkBeforeAndAfterServerRestartTests(string scenario)
           : base(scenario)
       {
       }

       protected override TestServiceClientContentStore CreateStore(
           AbsolutePath rootPath,
           ContentStoreConfiguration configuration,
           LocalServerConfiguration localContentServerConfiguration,
           TimeSpan? heartbeatOverride)
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

           return new TestServiceClientContentStore(
               Logger,
               FileSystem,
               new ServiceClientContentStoreConfiguration(CacheName, null, Scenario)
               {
                   RetryCount = RetryCount,
                   RetryIntervalSeconds = RetrySeconds,
               }, 
               null,
               serviceConfiguration,
               localContentServerConfiguration);
       }
    }
}
