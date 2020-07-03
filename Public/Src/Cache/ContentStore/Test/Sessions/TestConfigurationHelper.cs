// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

        public static LocalServerConfiguration LocalContentServerConfiguration { get; } = new LocalServerConfiguration();
    }
}
