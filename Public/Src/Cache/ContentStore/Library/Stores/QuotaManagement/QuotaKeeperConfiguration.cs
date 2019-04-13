// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// Configuration settings for <see cref="QuotaKeeper"/>.
    /// </summary>
    public sealed class QuotaKeeperConfiguration
    {
        /// <summary>
        /// <see cref="ContentStoreConfiguration.EnableElasticity"/>.
        /// </summary>
        public bool EnableElasticity { get; private set; }

        /// <summary>
        /// <see cref="ContentStoreConfiguration.MaxSizeQuota"/>.
        /// </summary>
        public MaxSizeQuota MaxSizeQuota { get; private set; }

        /// <summary>
        /// <see cref="ContentStoreConfiguration.DiskFreePercentQuota"/>.
        /// </summary>
        public DiskFreePercentQuota DiskFreePercentQuota { get; private set; }

        /// <summary>
        /// <see cref="ContentStoreConfiguration.InitialElasticSize"/>.
        /// </summary>
        public MaxSizeQuota InitialElasticSize { get; private set; }

        /// <summary>
        /// <see cref="ContentStoreConfiguration.HistoryWindowSize"/>.
        /// </summary>
        public int? HistoryWindowSize { get; private set; }

        /// <summary>
        /// <see cref="DistributedEvictionSettings"/>.
        /// </summary>
        public DistributedEvictionSettings DistributedEvictionSettings { get; private set; }

        /// <summary>
        /// Initial size of the content directory.
        /// </summary>
        public long ContentDirectorySize { get; private set; }

        /// <summary>
        /// If true, then quota keeper will check the current content directory size and start content eviction at startup if the threshold is reached.
        /// </summary>
        public bool StartPurgingAtStartup { get; private set; }

        /// <summary>
        /// If true, then <see cref="LegacyQuotaKeeper"/> is used, otherwise <see cref="QuotaKeeperV2"/> is used.
        /// </summary>
        public bool UseLegacyQuotaKeeper { get; private set; }

        /// <nodoc />
        private QuotaKeeperConfiguration()
        {
        }

        /// <nodoc />
        public static QuotaKeeperConfiguration Create(
            ContentStoreConfiguration configuration,
            DistributedEvictionSettings evictionSettings,
            ContentStoreSettings contentStoreSettings,
            long contentDirectorySize)
        {
            Contract.Requires(configuration != null);

            return new QuotaKeeperConfiguration()
                   {
                       EnableElasticity = configuration.EnableElasticity,
                       MaxSizeQuota = configuration.MaxSizeQuota,
                       DiskFreePercentQuota = configuration.DiskFreePercentQuota,
                       InitialElasticSize = configuration.InitialElasticSize,
                       HistoryWindowSize = configuration.HistoryWindowSize,
                       DistributedEvictionSettings = evictionSettings,
                       ContentDirectorySize = contentDirectorySize,
                       StartPurgingAtStartup = contentStoreSettings.StartPurgingAtStartup,
                       UseLegacyQuotaKeeper = contentStoreSettings.UseLegacyQuotaKeeperImplementation,
                   };
        }
    }
}
