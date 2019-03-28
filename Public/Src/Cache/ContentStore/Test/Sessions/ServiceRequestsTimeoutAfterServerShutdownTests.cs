// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using ContentStoreTest.Extensions;
using ContentStoreTest.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable SA1402 // File may only contain a single class

namespace ContentStoreTest.Sessions
{
    public abstract class ServiceRequestsTimeoutAfterServerShutdownTests<T> : ServiceClientContentSessionTestBase<T>
        where T : ServiceClientContentStore, ITestServiceClientContentStore
    {
        protected const uint RetrySeconds = 1;
        protected const uint RetryCount = 0; // No retries for performance reasons.

        protected ServiceRequestsTimeoutAfterServerShutdownTests(string scenario, ITestOutputHelper output)
            : base(scenario, output)
        {
        }

        [Fact]
        public Task PinTimesOutAfterServerShutdown()
        {
            return TimesOutAfterServerShutdown(async (context, session, contentHash) =>
                await session.PinAsync(context, contentHash, Token));
        }

        [Fact]
        public Task OpenStreamTimesOutAfterServerShutdown()
        {
            return TimesOutAfterServerShutdown(async (context, session, contentHash) =>
                await session.OpenStreamAsync(context, contentHash, Token));
        }

        [Fact]
        public async Task PlaceFileTimesOutAfterServerShutdown()
        {
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var path = directory.Path / "file.dat";
                await TimesOutAfterServerShutdown(async (context, session, contentHash) => await session.PlaceFileAsync(
                    context,
                    contentHash,
                    path,
                    FileAccessMode.ReadOnly,
                    FileReplacementMode.ReplaceExisting,
                    FileRealizationMode.Any,
                    Token));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task PutStreamTimesOutAfterServerShutdown(bool provideHash)
        {
            return TimesOutAfterServerShutdown(async (context, session, contentHash) =>
                await session.PutRandomAsync(context, ContentHashType, provideHash, ContentByteCount, Token));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task PutFileTimesOutAfterServerShutdown(bool provideHash)
        {
            return TimesOutAfterServerShutdown(async (context, session, contentHash) =>
                await session.PutRandomFileAsync(context, FileSystem, ContentHashType, provideHash, ContentByteCount, Token));
        }

        private Task TimesOutAfterServerShutdown(Func<Context, IContentSession, ContentHash, Task<ResultBase>> requestFunc)
        {
            return RunSessionTestAsync(ImplicitPin.None, async (context, session) =>
            {
                ITestServiceClientContentStore store = ((TestServiceClientContentSession)session).Store;
                await store.ShutdownServerAsync(context);

                var r = await requestFunc(context, session, ContentHash.Random());
                r.ErrorMessage.Should().Contain("service");
            });
        }
    }

    [Trait("Category", "Integration")]
    [Trait("Category", "Integration1")]
    [Trait("Category", "WindowsOSOnly")] // These use named event handles, which are not supported in .NET core
    public class InProcessServiceRequestsTimeoutAfterServerShutdownTests : ServiceRequestsTimeoutAfterServerShutdownTests<TestInProcessServiceClientContentStore>
    {
       public InProcessServiceRequestsTimeoutAfterServerShutdownTests(ITestOutputHelper output)
           : base(nameof(InProcessServiceRequestsTimeoutAfterServerShutdownTests), output)
       {
       }

       protected InProcessServiceRequestsTimeoutAfterServerShutdownTests(string scenario, ITestOutputHelper output)
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

           return new TestInProcessServiceClientContentStore(
               FileSystem,
               Logger,
               CacheName,
               Scenario,
               heartbeatOverride,
               serviceConfiguration,
               RetrySeconds,
               RetryCount,
               localContentServerConfiguration);
       }
    }

    [Trait("Category", "Integration")]
    [Trait("Category", "Integration1")]
    [Trait("Category", "QTestSkip")]
    /*public*/ class ExternalProcessServiceRequestsTimeoutAfterServerShutdownTests : ServiceRequestsTimeoutAfterServerShutdownTests<TestServiceClientContentStore>
    {
       public ExternalProcessServiceRequestsTimeoutAfterServerShutdownTests(ITestOutputHelper output)
           : base(nameof(ExternalProcessServiceRequestsTimeoutAfterServerShutdownTests), output)
       {
       }

       protected ExternalProcessServiceRequestsTimeoutAfterServerShutdownTests(string scenario, ITestOutputHelper output)
           : base(scenario, output)
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
               heartbeatOverride,
               serviceConfiguration,
               localContentServerConfiguration
               );
       }
    }
}
