// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.Monitor.App.Notifications;
using BuildXL.Cache.Monitor.Library.Client;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal class KustoRuleConfiguration
    {
        public IClock Clock { get; set; }

        public ILogger Logger { get; set; }

        public INotifier<Notification> Notifier { get; set; }

        public IKustoClient KustoClient { get; set; }

        public string KustoDatabaseName { get; set; }

        public KustoRuleConfiguration(
            IClock clock,
            ILogger logger,
            INotifier<Notification> notifier,
            IKustoClient kustoClient,
            string kustoDatabaseName)
        {
            Clock = clock;
            Logger = logger;
            Notifier = notifier;
            KustoClient = kustoClient;
            KustoDatabaseName = kustoDatabaseName;
        }

        public KustoRuleConfiguration(KustoRuleConfiguration other)
            : this (other.Clock, other.Logger, other.Notifier, other.KustoClient, other.KustoDatabaseName)
        {

        }
    }
}
