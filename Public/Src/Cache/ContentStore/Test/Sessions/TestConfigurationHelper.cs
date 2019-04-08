// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
