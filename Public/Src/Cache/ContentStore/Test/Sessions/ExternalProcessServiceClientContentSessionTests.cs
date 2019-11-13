// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using ContentStoreTest.Stores;
using System;
using System.Collections.Generic;
using ContentStoreTest.Extensions;
using Xunit;

#pragma warning disable SA1402 // File may only contain a single class
#pragma warning disable IDE0040 // Accessibility modifiers required

namespace ContentStoreTest.Sessions
{
    [Trait("Category", "Integration")]
    [Trait("Category", "Integration1")]
    [Trait("Category", "QTestSkip")]
    /*public*/ class ExternalProcessServiceClientContentSessionTests : ServiceClientContentSessionTests
    {
        private const int WaitForServerReadyTimeoutMs = 10000;
        private const int WaitForExitTimeoutMs = 30000;
        private const LocalServerConfiguration LocalContentServerConfiguration = null;

        public ExternalProcessServiceClientContentSessionTests()
            : base(nameof(ExternalProcessServiceClientContentSessionTests))
        {
        }

        protected override IContentStore CreateStore(DisposableDirectory testDirectory, ContentStoreConfiguration configuration)
        {
            var rootPath = testDirectory.Path;
            configuration.Write(FileSystem, rootPath).Wait();

            var grpcPortFileName = Guid.NewGuid().ToString();

            var serviceConfig = new ServiceConfiguration(
                new Dictionary<string, AbsolutePath> { { CacheName, rootPath } },
                rootPath,
                MaxConnections,
                GracefulShutdownSeconds,
                PortExtensions.GetNextAvailablePort(),
                grpcPortFileName);

            return new TestServiceClientContentStore(
                Logger,
                FileSystem,
                new ServiceClientContentStoreConfiguration(CacheName, rpcConfiguration: null, scenario: Scenario), 
                heartbeatInterval: null,
                serviceConfiguration: serviceConfig);
        }

        protected override IStartupShutdown CreateServer(ServiceConfiguration serviceConfiguration)
        {
            return new ServiceProcess(serviceConfiguration, LocalContentServerConfiguration, Scenario, WaitForServerReadyTimeoutMs, WaitForExitTimeoutMs);
        }
    }
}
