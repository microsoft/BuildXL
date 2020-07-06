// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Service;

namespace ContentStoreTest.Sessions
{
    public static class TestConfigurationHelper
    {
        public static LocalServerConfiguration CreateLocalContentServerConfiguration(ServiceConfiguration configuration) => new LocalServerConfiguration(configuration)
                                                                                        {
                                                                                            // Using lower number of request call tokens to make tests faster.
                                                                                            RequestCallTokensPerCompletionQueue = 10
                                                                                        };

        public static LocalServerConfiguration LocalContentServerConfiguration { get; } = CreateLocalContentServerConfiguration();

        public static LocalServerConfiguration CreateLocalContentServerConfiguration () => new LocalServerConfiguration(
            dataRootPath: new AbsolutePath("d:\\temp"),
            namedCacheRoots: new Dictionary<string, AbsolutePath>(),
            grpcPort: LocalServerConfiguration.DefaultGrpcPort,
            fileSystem: new PassThroughFileSystem());
    }
}
