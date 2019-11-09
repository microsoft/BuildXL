using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using Kusto.Data.Common;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal class LastProducedCheckpointRule : KustoRuleBase
    {
        public class Configuration : KustoRuleConfiguration
        {
            public Configuration(KustoRuleConfiguration kustoRuleConfiguration)
                : base(kustoRuleConfiguration)
            {
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromHours(2);

            public TimeSpan WarningThreshold { get; set; } = TimeSpan.FromMinutes(45);

            public TimeSpan ErrorThreshold { get; set; } = TimeSpan.FromHours(1);
        }

        private readonly Configuration _configuration;

        public override string Identifier => $"{nameof(LastProducedCheckpointRule)}:{_configuration.Environment}/{_configuration.Stamp}";

        public LastProducedCheckpointRule(Configuration configuration)
            : base(configuration)
        {
            Contract.RequiresNotNull(configuration);
            _configuration = configuration;
        }

#pragma warning disable CS0649
        private class Result
        {
            public DateTime PreciseTimeStamp;
            public TimeSpan Age;
            public string Machine;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            // NOTE(jubayard): When a summarize is run over an empty result set, Kusto produces a single (null) row,
            // which is why we need to filter it out.
            var now = _configuration.Clock.UtcNow - Constants.KustoIngestionDelay;
            var query =
                $@"
                let end = now() - {CslTimeSpanLiteral.AsCslString(Constants.KustoIngestionDelay)};
                let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                CloudBuildLogEvent
                | where PreciseTimeStamp between (start .. end)
                | where Stamp == ""{_configuration.Stamp}""
                | where Service == ""{Constants.MasterServiceName}""
                | where Message contains ""CreateCheckpointAsync stop""
                | summarize (PreciseTimeStamp, Machine)=arg_max(PreciseTimeStamp, Machine)
                | extend Age = end - PreciseTimeStamp
                | where not(isnull(PreciseTimeStamp))";
            var results = (await QuerySingleResultSetAsync<Result>(query)).ToList();

            if (results.Count == 0)
            {
                Emit(context, "NoLogs", Severity.Fatal,
                    $"No checkpoints produced for at least {_configuration.LookbackPeriod}",
                    eventTimeUtc: now);
                return;
            }

            var age = results[0].Age;
            Utilities.SeverityFromThreshold(age, _configuration.WarningThreshold, _configuration.ErrorThreshold, (severity, threshold) =>
            {
                Emit(context, "CreationThreshold", severity,
                    $"Newest checkpoint age `{age}` above threshold `{threshold}`. Master is {results[0].Machine}",
                    eventTimeUtc: results[0].PreciseTimeStamp);
            });
        }
    }
}
