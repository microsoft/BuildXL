// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.Monitor.App.Notifications;
using Kusto.Data.Common;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal class KustoRuleConfiguration
    {
        public KustoRuleConfiguration(IClock clock, ILogger logger, INotifier<Notification> notifier, ICslQueryProvider cslQueryProvider, StampId stampId, string kustoDatabaseName, string cacheTableName)
        {
            Clock = clock;
            Logger = logger;
            Notifier = notifier;
            CslQueryProvider = cslQueryProvider;
            StampId = stampId;
            KustoDatabaseName = kustoDatabaseName;
            CacheTableName = cacheTableName;
        }

        /// <summary>
        /// Convenience shallow copy constructor for easy inheritance construction.
        /// </summary>
        public KustoRuleConfiguration(KustoRuleConfiguration other)
        {
            Clock = other.Clock;
            Logger = other.Logger;
            Notifier = other.Notifier;
            CslQueryProvider = other.CslQueryProvider;
            KustoDatabaseName = other.KustoDatabaseName;
            StampId = other.StampId;
            CacheTableName = other.CacheTableName;
        }

        public IClock Clock { get; set; }

        public ILogger Logger { get; set; }

        public INotifier<Notification> Notifier { get; set; }

        public ICslQueryProvider CslQueryProvider { get; set; }

        public StampId StampId { get; set; }

        public string KustoDatabaseName { get; set; }

        public string CacheTableName { get; set; }

        public string Stamp => StampId.Name;

        public CloudBuildEnvironment Environment => StampId.Environment;
    }
}
