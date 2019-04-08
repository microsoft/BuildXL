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
    public abstract class InProcessServiceClientContentPerformanceTests : ContentPerformanceTests
    {
        private const uint GracefulShutdownSeconds = ServiceConfiguration.DefaultGracefulShutdownSeconds;
        private readonly uint _maxConnections;
        private readonly uint _connectionsPerSession;
        private readonly string _scenario;

        protected InProcessServiceClientContentPerformanceTests
            (
            ILogger logger,
            InitialSize initialSize,
            uint maxConnections,
            uint connectionsPerSession,
            PerformanceResultsFixture resultsFixture,
            string scenario,
            ITestOutputHelper output
            )
            : base(logger, initialSize, resultsFixture, output)
        {
            _maxConnections = maxConnections;
            _connectionsPerSession = connectionsPerSession;
            _scenario = scenario;
        }

        protected override IContentStore CreateStore(AbsolutePath rootPath, string cacheName, ContentStoreConfiguration configuration)
        {
            configuration.Write(FileSystem, rootPath).Wait();
            var grpcPortFileName = Guid.NewGuid().ToString();

            var serviceConfig = new ServiceConfiguration(
                new Dictionary<string, AbsolutePath> { { cacheName, rootPath } },
                rootPath,
                _maxConnections,
                GracefulShutdownSeconds,
                PortExtensions.GetNextAvailablePort(),
                grpcPortFileName);

            return new TestInProcessServiceClientContentStore(
                FileSystem, Logger, cacheName, _scenario, null, serviceConfig);
        }

        protected override Task<IReadOnlyList<ContentHash>> EnumerateContentHashesAsync(IReadOnlyContentSession session)
        {
            var testSession = (TestServiceClientContentSession)session;
            return testSession.EnumerateHashes();
        }
    }

    [Trait("Category", "QTestSkip")]
    [Trait("Category", "Performance")]
    public class FullInsensitiveInProcessServiceClientContentPerformanceTests
        : InProcessServiceClientContentPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        private const string Scenario = nameof(FullInsensitiveInProcessServiceClientContentPerformanceTests);
        private const uint MaxConnections = 16;
        private const uint ConnectionsPerSession = 16;

        public FullInsensitiveInProcessServiceClientContentPerformanceTests(PerformanceResultsFixture resultsFixture, ITestOutputHelper output)
            : base(TestGlobal.Logger, InitialSize.Full, MaxConnections, ConnectionsPerSession, resultsFixture, Scenario, output)
        {
        }

        [Fact]
        public Task EndToEnd()
        {
            return PinExisting();
        }
    }
}
