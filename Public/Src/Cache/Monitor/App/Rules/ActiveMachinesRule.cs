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

        public override async Task Run()
        {
            var ruleRunTimeUtc = _configuration.Clock.UtcNow;

            // NOTE(jubayard): When a summarize is run over an empty result set, Kusto produces a single (null) row,
            // which is why we need to filter it out.
            // NOTE(jubayard): The Kusto ingestion delay needs to be taken into consideration to avoid the number of
            // machines being reported as much less than they actually are.
            var query =
                $@"CloudBuildLogEvent
                   | where PreciseTimeStamp between (ago({CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)}) .. ago({CslTimeSpanLiteral.AsCslString(Constants.KustoIngestionDelay)}))
                   | where Service == ""{Constants.ServiceName}"" or Service == ""{Constants.MasterServiceName}""
                   | where Stamp == ""{_configuration.Stamp}""
                   | summarize ActiveMachines=dcount(Machine, {_configuration.DistinctCountPrecision}) by bin(PreciseTimeStamp, {CslTimeSpanLiteral.AsCslString(_configuration.BinningPeriod)})
                   | sort by PreciseTimeStamp asc
                   | where not(isnull(PreciseTimeStamp))";
            var results = (await QuerySingleResultSetAsync<Result>(query)).ToList();

            var now = _configuration.Clock.UtcNow;
            if (results.Count == 0)
            {
                Emit(Severity.Fatal,
                    $"Machines haven't produced any logs for at least `{_configuration.LookbackPeriod}`");
                return;
            }

            var detectionHorizon = now - _configuration.AnomalyDetectionHorizon;

            var training = new List<Result>();
            var prediction = new List<Result>();
            results.SplitBy(r => r.PreciseTimeStamp <= detectionHorizon, training, prediction);

            if (prediction.Count == 0)
            {
                Emit(Severity.Fatal,
                    $"Machines haven't produced any logs for at least `{_configuration.AnomalyDetectionHorizon}`");
                return;
            }

            if (training.Count < _configuration.MinimumTrainingPercentOfData * results.Count)
            {
                return;
            }

            var lookbackSizes = training.Select(r => r.ActiveMachines);
            double lookbackMin = Math.Ceiling((1 - _configuration.MaximumGrowthWrtToLookback) * lookbackSizes.Min());
            double lookbackMax = Math.Floor((1 + _configuration.MaximumGrowthWrtToLookback) * lookbackSizes.Max());
            var range2 = new CheckRange<double>(Comparer<double>.Default, lookbackMin, lookbackMax);
            range2.Check(prediction.Select(r => (double)r.ActiveMachines), (index, valueDouble) => {
                var value = (long)Math.Ceiling(valueDouble);

                Emit(Severity.Warning,
                    $"`{value}` active machines, outside of expected range [`{lookbackMin}`, `{lookbackMax}`]",
                    eventTimeUtc: prediction[index].PreciseTimeStamp);
            });
        }
    }
}
