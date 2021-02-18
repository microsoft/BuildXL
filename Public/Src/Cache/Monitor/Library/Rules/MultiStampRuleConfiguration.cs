// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.Monitor.App.Notifications;
using BuildXL.Cache.Monitor.Library.Client;
using BuildXL.Cache.Monitor.Library.IcM;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal class MultiStampRuleConfiguration : KustoRuleConfiguration
    {
        public MonitorEnvironment Environment { get; }

        public Watchlist WatchList { get; }

        public MultiStampRuleConfiguration(
            IClock clock,
            ILogger logger,
            INotifier<Notification> notifier,
            IKustoClient kustoClient,
            IIcmClient icmClient,
            string kustoDatabaseName,
            string cacheTableName,
            MonitorEnvironment environment,
            Watchlist watchlist)
            : base(clock ,logger, notifier, kustoClient, kustoDatabaseName, cacheTableName, icmClient)
        {
            Environment = environment;
            WatchList = watchlist;
        }

        /// <summary>
        /// Convenience shallow copy constructor for easy inheritance construction.
        /// </summary>
        public MultiStampRuleConfiguration(MultiStampRuleConfiguration other)
            : base(other)
        {
            Environment = other.Environment;
            WatchList = other.WatchList;
        }
    }
}
