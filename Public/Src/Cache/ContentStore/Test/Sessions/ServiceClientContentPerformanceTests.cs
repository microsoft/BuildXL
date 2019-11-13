// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using ContentStoreTest.Extensions;
using ContentStoreTest.Sessions;
using ContentStoreTest.Stores;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable SA1402 // File may only contain a single class

namespace ContentStoreTest.Performance.Sessions
{
    [Trait("Category", "WindowsOSOnly")] // These use named event handles, which are not supported in .NET core
    public abstract class ServiceClientContentPerformanceTests : ContentPerformanceTests
    {
        private const uint GracefulShutdownSeconds = ServiceConfiguration.DefaultGracefulShutdownSeconds;
        private const uint MaxConnections = 10;
        private readonly string _scenario;

        protected ServiceClientContentPerformanceTests(
            ILogger logger, InitialSize initialSize, PerformanceResultsFixture resultsFixture, string scenario, ITestOutputHelper output = null)
            : base(logger, initialSize, resultsFixture, output)
        {
            _scenario = scenario;
        }

        protected override IContentStore CreateStore(AbsolutePath rootPath, string cacheName, ContentStoreConfiguration configuration)
        {
            configuration.Write(FileSystem, rootPath).Wait();
            var grpcPortFileName = Guid.NewGuid().ToString();

            var serviceConfiguration = new ServiceConfiguration(
                new Dictionary<string, AbsolutePath> { { cacheName, rootPath } },
                rootPath,
                MaxConnections,
                GracefulShutdownSeconds,
                PortExtensions.GetNextAvailablePort(),
                grpcPortFileName);

            return new TestServiceClientContentStore(
                Logger,
                FileSystem,
                new ServiceClientContentStoreConfiguration(cacheName, null, _scenario), 
                null,
                serviceConfiguration);
        }

        protected override Task<IReadOnlyList<ContentHash>> EnumerateContentHashesAsync(IReadOnlyContentSession session)
        {
            var testSession = (TestServiceClientContentSession)session;
            return testSession.EnumerateHashes();
        }
    }

    [Trait("Category", "Performance")]
    [Trait("Category", "WindowsOSOnly")] // These use named event handles, which are not supported in .NET core
    public class FullServiceClientContentPerformanceTests
       : ServiceClientContentPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
       private const string Scenario = nameof(FullServiceClientContentPerformanceTests);

       public FullServiceClientContentPerformanceTests(PerformanceResultsFixture resultsFixture, ITestOutputHelper output)
           : base(TestGlobal.Logger, InitialSize.Full, resultsFixture, Scenario, output)
       {
       }
    }
}
