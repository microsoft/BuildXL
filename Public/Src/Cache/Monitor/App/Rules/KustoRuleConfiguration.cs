using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.Monitor.App.Notifications;
using Kusto.Data.Common;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal class KustoRuleConfiguration
    {
        public KustoRuleConfiguration()
        {
        }

        /// <summary>
        /// Convenience shallow copy constructor for easy inheritance construction.
        /// </summary>
        public KustoRuleConfiguration(KustoRuleConfiguration other)
        {
            Contract.RequiresNotNull(other);

            Clock = other.Clock;
            Logger = other.Logger;
            Notifier = other.Notifier;
            CslQueryProvider = other.CslQueryProvider;
            KustoDatabaseName = other.KustoDatabaseName;
            Environment = other.Environment;
            Stamp = other.Stamp;
        }

        public IClock Clock { get; set; }

        public ILogger Logger { get; set; }

        public INotifier Notifier { get; set; }

        public ICslQueryProvider CslQueryProvider { get; set; }

        public string KustoDatabaseName { get; set; }

        public Env Environment { get; set; }

        public string Stamp { get; set; }
    }
}
