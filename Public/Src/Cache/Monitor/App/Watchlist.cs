using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Notifications;
using Kusto.Data.Common;

#nullable enable

namespace BuildXL.Cache.Monitor.App
{
    /// <summary>
    /// Stores dynamic properties regarding the stamps and environments under watch. These are loaded from Kusto on
    /// demand and can be refreshed at any time.
    /// </summary>
    internal sealed class Watchlist
    {
        public struct Entry
        {
            public Env Environment;
            public string Stamp;
        }

        private struct Properties
        {
            public string Ring;
            public string CacheTableName;
        }

        private IReadOnlyDictionary<Entry, Properties> _properties = new Dictionary<Entry, Properties>();

        public IEnumerable<Entry> Entries => _properties.Keys;

        private readonly ILogger _logger;
        private readonly ICslQueryProvider _cslQueryProvider;

        private Watchlist(ILogger logger, ICslQueryProvider cslQueryProvider)
        {
            _logger = logger;
            _cslQueryProvider = cslQueryProvider;
        }

        public async Task RefreshAsync()
        {
            _properties = await LoadWatchlistAsync();
        }

        public static async Task<Watchlist> CreateAsync(ILogger logger, ICslQueryProvider cslQueryProvider)
        {
            var watchlist = new Watchlist(logger, cslQueryProvider);
            await watchlist.RefreshAsync();
            return watchlist;
        }

#pragma warning disable CS0649
        private class LoadResult
        {
            public string? Stamp;
            public string? Ring;
            public string? CacheTableName;
        }
#pragma warning restore CS0649

        private async Task<IReadOnlyDictionary<Entry, Properties>> LoadWatchlistAsync()
        {
            var query = @"CacheMonitorWatchlist";
            var watchlist = new Dictionary<Entry, Properties>();

            foreach (var environment in new[] { Env.CI, Env.Test, Env.Production })
            {
                _logger.Info("Loading monitor stamps for environment `{0}`", environment);

                var results = await _cslQueryProvider.QuerySingleResultSetAsync<LoadResult>(query, Monitor.EnvironmentToKustoDatabaseName[environment]);
                foreach (var result in results)
                {
                    Contract.AssertNotNullOrEmpty(result.Stamp);
                    Contract.AssertNotNullOrEmpty(result.Ring);
                    Contract.AssertNotNullOrEmpty(result.CacheTableName);

                    var entry = new Entry()
                    {
                        Environment = environment,
                        Stamp = result.Stamp,
                    };

                    watchlist[entry] = new Properties()
                    {
                        Ring = result.Ring,
                        CacheTableName = result.CacheTableName,
                    };

                    _logger.Info("Monitoring stamp `{0}` on ring `{1}` from table `{2}` in environment `{3}`", result.Stamp, result.Ring, result.CacheTableName, environment);
                }
            }

            return watchlist;
        }

        public bool TryGetCacheTableName(Entry entry, out string cacheTableName)
        {
            if (_properties.TryGetValue(entry, out var properties))
            {
                cacheTableName = properties.CacheTableName;
                return true;
            }

            cacheTableName = string.Empty;
            return false;
        }
    }
}
