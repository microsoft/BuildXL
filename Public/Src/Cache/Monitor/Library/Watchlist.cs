// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.Monitor.Library.Client;

namespace BuildXL.Cache.Monitor.App
{

    /// <summary>
    /// Stores dynamic properties regarding the stamps and environments under watch. These are loaded from Kusto on
    /// demand and can be refreshed at any time.
    /// </summary>
    internal sealed class Watchlist
    {
#pragma warning disable CS0649
        /// <remarks>
        /// WARNING: this class maps one to one to the result of the CacheMonitorWatchlist function. If changing,
        /// update both to ensure nothing breaks.
        /// </remarks>
        public class DynamicStampProperties : IEquatable<DynamicStampProperties>
        {
            public string Stamp = string.Empty;

            public string Ring = string.Empty;

            public string CacheTableName = string.Empty;

            public bool RedisAutoscalingEnabled;

            public long RedisAutoscalingMaximumClusterMemoryAllowedMb;

            public override bool Equals(object? obj)
            {
                return obj is DynamicStampProperties properties && Equals(properties);
            }

            public bool Equals(DynamicStampProperties? properties)
            {
                return properties != null &&
                       Stamp.Equals(properties.Stamp, StringComparison.InvariantCultureIgnoreCase) &&
                       Ring.Equals(properties.Ring, StringComparison.InvariantCultureIgnoreCase) &&
                       CacheTableName.Equals(properties.CacheTableName, StringComparison.InvariantCultureIgnoreCase) &&
                       RedisAutoscalingEnabled == properties.RedisAutoscalingEnabled &&
                       RedisAutoscalingMaximumClusterMemoryAllowedMb == properties.RedisAutoscalingMaximumClusterMemoryAllowedMb;
            }

            public override int GetHashCode()
            {
                return (Stamp, Ring, CacheTableName, RedisAutoscalingEnabled, RedisAutoscalingMaximumClusterMemoryAllowedMb).GetHashCode();
            }
        }
#pragma warning restore CS0649

        private IReadOnlyDictionary<StampId, DynamicStampProperties> _properties = new Dictionary<StampId, DynamicStampProperties>();

        public IEnumerable<StampId> Stamps => _properties.Keys;

        public IEnumerable<KeyValuePair<StampId, DynamicStampProperties>> Entries => _properties;

        public IEnumerable<CloudBuildEnvironment> Environments => _environments.Select(kvp => kvp.Key);

        public IReadOnlyDictionary<CloudBuildEnvironment, List<StampId>> EnvStamps =>
            _properties.GroupBy(property => property.Key.Environment, property => property.Key)
            .ToDictionary(group => group.Key, group => group.ToList());

        private readonly ILogger _logger;
        private readonly IKustoClient _kustoClient;
        private readonly IReadOnlyDictionary<CloudBuildEnvironment, EnvironmentConfiguration> _environments;

        private Watchlist(ILogger logger, IKustoClient kustoClient, IReadOnlyDictionary<CloudBuildEnvironment, EnvironmentConfiguration> environments)
        {
            _logger = logger;
            _kustoClient = kustoClient;
            _environments = environments;
        }

        /// <summary>
        /// Reloads the watchlist and settings for all stamps
        /// </summary>
        /// <returns>true iff the refresh caused a change in either the stamps being watched or the properties for any stamp</returns>
        public async Task<bool> RefreshAsync()
        {
            var newProperties = await LoadWatchlistAsync();

            // The properties are immutable, so this is mostly a formality to ensure that any threads that may be
            // concurrently accessing the watchlist do so in a well-defined way.
            var oldProperties = Interlocked.Exchange(ref _properties, newProperties);

            Func<KeyValuePair<StampId, DynamicStampProperties>, string> extract = kvp => kvp.Key.ToString();
            bool hasNotChanged = oldProperties.OrderBy(extract).SequenceEqual(newProperties.OrderBy(extract));

            return !hasNotChanged;
        }

        public static async Task<Watchlist> CreateAsync(ILogger logger, IKustoClient kustoClient, IReadOnlyDictionary<CloudBuildEnvironment, EnvironmentConfiguration> environments)
        {
            var watchlist = new Watchlist(logger, kustoClient, environments);
            await watchlist.RefreshAsync();
            return watchlist;
        }

        private async Task<IReadOnlyDictionary<StampId, DynamicStampProperties>> LoadWatchlistAsync()
        {
            var query = @"CacheMonitorWatchlist";
            var watchlist = new Dictionary<StampId, DynamicStampProperties>();

            // This runs a set of blocking Kusto queries, which is pretty slow, so it's done concurrently
            await _environments.ParallelForEachAsync(async (keyValuePair) => {
                var envName = keyValuePair.Key;
                var envConf = keyValuePair.Value;

                _logger.Info("Loading monitor stamps for environment `{0}`", envName);

                var results = await _kustoClient.QueryAsync<DynamicStampProperties>(query, envConf.KustoDatabaseName);
                foreach (var result in results)
                {
                    Contract.AssertNotNullOrEmpty(result.Stamp);
                    Contract.AssertNotNullOrEmpty(result.Ring);
                    Contract.AssertNotNullOrEmpty(result.CacheTableName);

                    var entry = new StampId(envName, result.Stamp);
                    lock (watchlist) {
                        watchlist[entry] = result;
                    }

                    _logger.Debug("Monitoring stamp `{0}` on ring `{1}` from table `{2}` in environment `{3}`", result.Stamp, result.Ring, result.CacheTableName, envName);
                }
            });

            return watchlist;
        }

        public Result<DynamicStampProperties> TryGetProperties(StampId stampId)
        {
            if (_properties.TryGetValue(stampId, out var properties))
            {
                return properties;
            }

            return new Result<DynamicStampProperties>(errorMessage: $"Unknown stamp {stampId}");
        }
    }
}
