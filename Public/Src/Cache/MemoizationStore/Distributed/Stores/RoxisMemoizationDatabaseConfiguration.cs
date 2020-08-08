using System;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.Roxis.Client;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <summary>
    /// Configuration for <see cref="RoxisMemoizationDatabase"/>
    /// </summary>
    public class RoxisMemoizationDatabaseConfiguration : MemoizationStoreConfiguration
    {
        /// <nodoc />
        public RoxisClientConfiguration MetadataClientConfiguration { get; set; } = new RoxisClientConfiguration();

        /// <nodoc />
        public TimeSpan? DefaultTimeToLive { get; set; } = TimeSpan.FromMinutes(60);

        /// <inheritdoc />
        public override IMemoizationStore CreateStore(ILogger logger, IClock clock)
        {
            var client = new RoxisClient(MetadataClientConfiguration);
            return new DatabaseMemoizationStore(new RoxisMemoizationDatabase(this, client, clock));
        }
    }
}
