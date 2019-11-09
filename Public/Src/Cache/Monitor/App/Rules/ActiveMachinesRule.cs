using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Analysis;
using Kusto.Data.Common;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal class ActiveMachinesRule : KustoRuleBase
    {
        public class Configuration : KustoRuleConfiguration
        {
            public Configuration(KustoRuleConfiguration kustoRuleConfiguration)
                : base(kustoRuleConfiguration)
            {
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromHours(12);

            public TimeSpan AnomalyDetectionHorizon { get; set; } = TimeSpan.FromHours(1);

            public int DistinctCountPrecision { get; set; } = 1;

            public TimeSpan BinningPeriod { get; set; } = TimeSpan.FromMinutes(10);

            /// <summary>
            /// Minimum percentage of data needed to estimate the maximum/minimum ranges
            /// </summary>
            public double MinimumTrainingPercentOfData { get; set; } = 0.2;

            /// <summary>
            /// Maximum amount that a checkpoint can grow or decrease in size with respect to lookback period
            /// </summary>
            public double MaximumGrowthWrtToLookback { get; set; } = 0.1;
        }

        private readonly Configuration _configuration;

        public override string Identifier => $"{nameof(ActiveMachinesRule)}:{_configuration.Environment}/{_configuration.Stamp}";

        public ActiveMachinesRule(Configuration configuration)
            : base(configuration)
        {
            Contract.RequiresNotNull(configuration);
            _configuration = configuration;
        }

#pragma warning disable CS0649
        private class Result
        {
            public DateTime PreciseTimeStamp;
            public long ActiveMachines;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            // NOTE(jubayard): When a summarize is run over an empty result set, Kusto produces a single (null) row,
            // which is why we need to filter it out.
            // NOTE(jubayard): The Kusto ingestion delay needs to be taken into consideration to avoid the number of
            // machines being reported as much less than they actually are.
            var now = _configuration.Clock.UtcNow - Constants.KustoIngestionDelay;
            var query =
                $@"
                let end = now() - {CslTimeSpanLiteral.AsCslString(Constants.KustoIngestionDelay)};
                let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                CloudBuildLogEvent
                | where PreciseTimeStamp between (start .. end)
                | where Service == ""{Constants.ServiceName}"" or Service == ""{Constants.MasterServiceName}""
                | where Stamp == ""{_configuration.Stamp}""
                | summarize ActiveMachines=dcount(Machine, {_configuration.DistinctCountPrecision}) by bin(PreciseTimeStamp, {CslTimeSpanLiteral.AsCslString(_configuration.BinningPeriod)})
                | sort by PreciseTimeStamp asc
                | where not(isnull(PreciseTimeStamp))";
            var results = (await QuerySingleResultSetAsync<Result>(query)).ToList();

            if (results.Count == 0)
            {
                Emit(context, "NoLogs", Severity.Fatal,
                    $"Machines haven't produced any logs for at least `{_configuration.LookbackPeriod}`",
                    eventTimeUtc: now);
                return;
            }

            var detectionHorizon = now - _configuration.AnomalyDetectionHorizon;

            var training = new List<Result>();
            var prediction = new List<Result>();
            results.SplitBy(r => r.PreciseTimeStamp <= detectionHorizon, training, prediction);

            if (prediction.Count == 0)
            {
                Emit(context, "NoLogs", Severity.Fatal,
                    $"Machines haven't produced any logs for at least `{_configuration.AnomalyDetectionHorizon}`",
                    eventTimeUtc: now);
                return;
            }

            if (training.Count < _configuration.MinimumTrainingPercentOfData * results.Count)
            {
                return;
            }

            var lookbackSizes = training.Select(r => r.ActiveMachines);
            var lookbackMin = (long)Math.Ceiling((1 - _configuration.MaximumGrowthWrtToLookback) * lookbackSizes.Min());
            var lookbackMax = (long)Math.Floor((1 + _configuration.MaximumGrowthWrtToLookback) * lookbackSizes.Max());
            prediction.Select(p => p.ActiveMachines).NotInRange(lookbackMin, lookbackMax).Perform(index => {
                Emit(context, "ExpectedRange", Severity.Warning,
                    $"`{prediction[index].ActiveMachines}` active machines, outside of expected range [`{lookbackMin}`, `{lookbackMax}`]",
                    eventTimeUtc: prediction[index].PreciseTimeStamp);
            });
        }
    }
}
