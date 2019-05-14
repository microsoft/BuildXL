// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <nodoc />
    public enum QuotaKeeperCounters
    {
        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        PurgeCall = 1,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        ProcessQuotaRequest,
    }

    /// <summary>
    /// The entity that maintains and enforces the content quota.
    /// </summary>
    public abstract class QuotaKeeper : StartupShutdownBase
    {
        /// <nodoc />
        protected readonly bool PurgeAtStartup;

        /// <summary>
        ///     Public name for monitoring use.
        /// </summary>
        public const string Component = "QuotaKeeper";

        /// <nodoc />
        protected QuotaKeeper(QuotaKeeperConfiguration configuration)
        {
            PurgeAtStartup = configuration.StartPurgingAtStartup;
        }

        /// <nodoc />
        public static QuotaKeeper Create(
            IAbsFileSystem fileSystem,
            ContentStoreInternalTracer tracer,
            CancellationToken token,
            IContentStoreInternal store,
            QuotaKeeperConfiguration configuration)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(tracer != null);
            Contract.Requires(configuration != null);

            if (configuration.UseLegacyQuotaKeeper)
            {
                return new LegacyQuotaKeeper(fileSystem, tracer, configuration, token, store);
            }

            return new QuotaKeeperV2(fileSystem, tracer, configuration, token, store);
        }

        /// <summary>
        /// Gets performances counters for a current instance.
        /// </summary>
        public virtual CounterCollection<QuotaKeeperCounters> Counters => null;

        /// <summary>
        /// Gets the current number of content bytes.
        /// </summary>
        public abstract long CurrentSize { get; }

        /// <summary>
        /// Completes all the pending operations (like reservation and/or calibration requests).
        /// </summary>
        public abstract Task SyncAsync(Context context, bool purge);

        /// <summary>
        /// Reserve room for specified content size.
        /// </summary>
        /// <exception cref="CacheException">The exception is thrown if the reservation fails.</exception>
        public abstract Task<ReserveTransaction> ReserveAsync(long contentSize);

        /// <summary>
        /// Forces all the existing rules to calibrate themselves.
        /// </summary>
        public abstract void Calibrate();

        /// <nodoc />
        protected internal abstract Task<EvictResult> EvictContentAsync(
            Context context,
            ContentHashWithLastAccessTimeAndReplicaCount contentHashInfo,
            bool onlyUnlinked);

        /// <summary>
        /// Notifies the keeper that content of a given size is evicted.
        /// </summary>
        public abstract void OnContentEvicted(long size);

        /// <nodoc />
        protected List<IQuotaRule> CreateRules(
            IAbsFileSystem fileSystem,
            QuotaKeeperConfiguration configuration,
            IContentStoreInternal store)
        {
            var rules = new List<IQuotaRule>();
            var distributedEvictionSettings = configuration.DistributedEvictionSettings;

            if (configuration.EnableElasticity)
            {
                var elasticSizeRule = new ElasticSizeRule(
                    configuration.HistoryWindowSize,
                    configuration.InitialElasticSize,
                    EvictContentAsync,
                    () => CurrentSize,
                    store.ReadPinSizeHistory,
                    fileSystem,
                    store.RootPath,
                    distributedEvictionSettings: distributedEvictionSettings);
                rules.Add(elasticSizeRule);
            }
            else
            {
                if (configuration.MaxSizeQuota != null)
                {
                    rules.Add(new MaxSizeRule(configuration.MaxSizeQuota, EvictContentAsync, () => CurrentSize, distributedEvictionSettings));
                }

                if (configuration.DiskFreePercentQuota != null)
                {
                    rules.Add(new DiskFreePercentRule(configuration.DiskFreePercentQuota, EvictContentAsync, fileSystem, store.RootPath, distributedEvictionSettings));
                }
            }

            if (!rules.Any())
            {
                throw new CacheException("At least one quota rule must be defined");
            }

            return rules;
        }

        /// <summary>
        /// Returns true if the purge process should be stopped.
        /// </summary>
        internal abstract bool StopPurging(out string stopReason, out IQuotaRule activeRule);
    }
}
