using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using Kusto.Ingest;

namespace BuildXL.Cache.Monitor.App.Notifications
{
    internal class KustoNotifierSettings
    {
        public Severity MinimumFlushSeverity { get; set; } = Severity.Warning;

        public TimeSpan MaximumFlushInterval { get; set; } = TimeSpan.FromMinutes(5);

        public int MaximumPendingNotifications { get; set; }
    }

    internal class KustoNotifier : INotifier
    {
        private readonly KustoNotifierSettings _settings;
        private readonly IKustoIngestClient _kustoIngestClient;

        public KustoNotifier(KustoNotifierSettings settings, IKustoIngestClient kustoIngestClient)
        {
            Contract.RequiresNotNull(settings);
            Contract.RequiresNotNull(kustoIngestClient);

            _settings = settings;
            _kustoIngestClient = kustoIngestClient;
        }

        public void Emit(Notification notification)
        {
            Contract.RequiresNotNull(notification);

            if (ShouldForceFlush(notification))
            {

            }

            throw new NotImplementedException();
        }

        private bool ShouldForceFlush(Notification notification)
        {
            return notification.Severity >= _settings.MinimumFlushSeverity;
        }
    }
}
