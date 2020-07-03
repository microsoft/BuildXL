using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using Kusto.Data.Common;
using static BuildXL.Cache.Monitor.App.Utilities;

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

            public Thresholds<TimeSpan> AgeThresholds = new Thresholds<TimeSpan>()
            {
                Warning = TimeSpan.FromMinutes(45),
                Error = TimeSpan.FromHours(1),
            };
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
            var now = _configuration.Clock.UtcNow;
            var query =
                $@"
                let end = now();
                let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                table(""{_configuration.CacheTableName}"")
                | where PreciseTimeStamp between (start .. end)
                | where Stamp == ""{_configuration.Stamp}""
                | where Service == ""{Constants.MasterServiceName}""
                | where Message contains ""CreateCheckpointAsync stop""
                | summarize (PreciseTimeStamp, Machine)=arg_max(PreciseTimeStamp, Machine)
                | extend Age = end - PreciseTimeStamp
                | where not(isnull(PreciseTimeStamp))";
            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

            if (results.Count == 0)
            {
                Emit(context, "NoLogs", Severity.Fatal,
                    $"No checkpoints produced for at least {_configuration.LookbackPeriod}",
                    eventTimeUtc: now);
                return;
            }

            var age = results[0].Age;
            _configuration.AgeThresholds.Check(age, (severity, threshold) =>
            {
                Emit(context, "CreationThreshold", severity,
                    $"Newest checkpoint age `{age}` above threshold `{threshold}`. Master is {results[0].Machine}",
                    eventTimeUtc: results[0].PreciseTimeStamp);
            });
        }
    }
}
