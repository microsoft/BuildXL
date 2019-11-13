// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using ContentStoreTest.Extensions;
using ContentStoreTest.Stores;
using Xunit;

#pragma warning disable SA1402 // File may only contain a single class
#pragma warning disable IDE0040 // Accessibility modifiers required

namespace ContentStoreTest.Sessions
{
    [Trait("Category", "Integration")]
    [Trait("Category", "Integration1")]
    [Trait("Category", "QTestSkip")]
    /*public*/ class InProcessServiceClientContentSessionTests : ServiceClientContentSessionTests
    {
        public InProcessServiceClientContentSessionTests()
            : base(nameof(InProcessServiceClientContentSessionTests))
        {
        }

        protected override IContentStore CreateStore(DisposableDirectory testDirectory, ContentStoreConfiguration configuration)
        {
            var rootPath = testDirectory.Path;
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
                null,
                serviceConfiguration
                );
        }

        protected override IStartupShutdown CreateServer(ServiceConfiguration serviceConfiguration)
        {
            return new LocalContentServer(FileSystem, Logger, Scenario, path =>
                new FileSystemContentStore(FileSystem, SystemClock.Instance, path), TestConfigurationHelper.CreateLocalContentServerConfiguration(serviceConfiguration));
        }
    }
}
