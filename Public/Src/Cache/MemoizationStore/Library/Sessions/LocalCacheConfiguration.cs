// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Stores;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Sessions
{
    /// <summary>
    /// Cache configuration for <see cref="LocalCache"/>.
    /// </summary>
    /// <remarks>
    /// This class is here for backwards compatibility only. It is only a wrapper around
    /// <see cref="ServiceClientContentStoreConfiguration"/>.
    /// </remarks>
    public class LocalCacheConfiguration
    {
        /// <nodoc />
        public ServiceClientContentStoreConfiguration? ServiceClientContentStoreConfiguration { get; set; } = null;

        /// <summary>
        /// Create a local cache configuration which does not have any distributed features.
        /// </summary>
        public static LocalCacheConfiguration CreateServerDisabled()
        {
            return new LocalCacheConfiguration();
        }

        /// <nodoc />
        public LocalCacheConfiguration(ServiceClientContentStoreConfiguration? serviceClientContentStoreConfiguration = null)
        {
            ServiceClientContentStoreConfiguration = serviceClientContentStoreConfiguration;
        }
    }
}
