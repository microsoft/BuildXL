// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.Monitor.App.Notifications;
using BuildXL.Cache.Monitor.Library.Client;
using BuildXL.Cache.Monitor.Library.IcM;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal class SingleStampRuleConfiguration : KustoRuleConfiguration
    {
        public StampId StampId { get; }

        public string Stamp => StampId.Name;

        public MonitorEnvironment Environment => StampId.Environment;

        public SingleStampRuleConfiguration(
            IClock clock,
            ILogger logger,
            INotifier<Notification> notifier,
            IKustoClient kustoClient,
            IIcmClient icmClient,
            string kustoDatabaseName,
            StampId stamp)
            : base(clock, logger, notifier, kustoClient, kustoDatabaseName, icmClient)
        {
            StampId = stamp;
        }

        public SingleStampRuleConfiguration(SingleStampRuleConfiguration other) : base(other)
        {
            StampId = other.StampId;
        }
    }
}
