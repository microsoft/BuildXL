using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

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

            public TimeSpan WarningAge { get; set; } = TimeSpan.FromMinutes(30);

            public TimeSpan ErrorAge { get; set; } = TimeSpan.FromMinutes(45);
        }

        private readonly Configuration _configuration;

        public override string Identifier => $"{nameof(LastProducedCheckpointRule)}:{_configuration.Environment}/{_configuration.Stamp}";

        public LastProducedCheckpointRule(Configuration configuration)
            : base(configuration)
        {
            Contract.RequiresNotNull(configuration);
            _configuration = configuration;
        }

        private class Result
        {
            public DateTime PreciseTimeStamp;
        }

        public override async Task Run()
        {
            var ruleRunTimeUtc = _configuration.Clock.UtcNow;

            // NOTE(jubayard): When a summarize is run over an empty result set, Kusto produces a single (null) row,
            // which is why we need to filter it out.
            var query =
                $@"CloudBuildLogEvent
                | where PreciseTimeStamp > ago(1h)
                | where Stamp == ""{_configuration.Stamp}""
                | where Service == ""{Constants.MasterServiceName}""
                | where Message contains ""CreateCheckpointAsync stop""
                | project PreciseTimeStamp
                | summarize PreciseTimeStamp=max(PreciseTimeStamp)
                | where not(isnull(PreciseTimeStamp))";
            var results = (await QuerySingleResultSetAsync<Result>(query)).ToList();

            var now = _configuration.Clock.UtcNow;
            if (results.Count == 0)
            {
                Emit(Severity.Fatal,
                    $"Master hasn't produced checkpoints for over an hour",
                    ruleRunTimeUtc: ruleRunTimeUtc,
                    eventTimeUtc: now);
            }
            else
            {
                var age = now - results[0].PreciseTimeStamp;

                if (age >= _configuration.WarningAge)
                {
                    var severity = Severity.Warning;
                    var threshold = _configuration.WarningAge;
                    if (age >= _configuration.ErrorAge)
                    {
                        severity = Severity.Error;
                        threshold = _configuration.ErrorAge;
                    }

                    Emit(severity,
                        $"Checkpoint age `{age}` is above acceptable threshold `{threshold}`",
                        ruleRunTimeUtc: ruleRunTimeUtc,
                        eventTimeUtc: results[0].PreciseTimeStamp);
                }
            }
        }
    }
}
