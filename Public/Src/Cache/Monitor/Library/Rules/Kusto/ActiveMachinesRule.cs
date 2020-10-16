// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Analysis;
using BuildXL.Cache.Monitor.App.Scheduling;
using BuildXL.Cache.Monitor.Library.Rules;
using Kusto.Data.Common;

namespace BuildXL.Cache.Monitor.App.Rules.Kusto
{
    internal class ActiveMachinesRule : MultipleStampRuleBase
    {
        public class Configuration : MultiStampRuleConfiguration
        {
            public Configuration(MultiStampRuleConfiguration kustoRuleConfiguration)
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

        /// <inheritdoc />
        public override string Identifier => $"{nameof(ActiveMachinesRule)}:{_configuration.Environment}";

        public ActiveMachinesRule(Configuration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

#pragma warning disable CS0649
        internal class Result
        {
            public DateTime PreciseTimeStamp;
            public long ActiveMachines;
            public string Stamp = string.Empty;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            // NOTE(jubayard): The Kusto ingestion delay needs to be taken into consideration to avoid the number of
            // machines being reported as much less than they actually are.
            var now = _configuration.Clock.UtcNow - Constants.KustoIngestionDelay;
            var query =
                $@"
                let end = now() - {CslTimeSpanLiteral.AsCslString(Constants.KustoIngestionDelay)};
                let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                table(""{_configuration.CacheTableName}"")
                | where PreciseTimeStamp between (start .. end)
                | summarize ActiveMachines=dcount(Machine, {_configuration.DistinctCountPrecision}) by Stamp, bin(PreciseTimeStamp, {CslTimeSpanLiteral.AsCslString(_configuration.BinningPeriod)})
                | sort by PreciseTimeStamp asc
                | where not(isnull(PreciseTimeStamp))";
            var results = (await QueryKustoAsync<Result>(context, query)).ToList();
            var detectionHorizon = now - _configuration.AnomalyDetectionHorizon;

            GroupByStampAndCallHelper<Result>(results, result => result.Stamp, activeMachineRuleHelper);

            void activeMachineRuleHelper(string stamp, List<Result> results)
            {
                var training = new List<Result>();
                var prediction = new List<Result>();
                results.SplitBy(r => r.PreciseTimeStamp <= detectionHorizon, training, prediction);

                if (prediction.Count == 0)
                {
                    Emit(context, "NoLogs", Severity.Fatal,
                        $"Machines haven't produced any logs for at least `{_configuration.AnomalyDetectionHorizon}`",
                        stamp,
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
                prediction.Select(p => p.ActiveMachines).NotInRange(lookbackMin, lookbackMax).Perform(index =>
                {
                    Emit(context, "ExpectedRange", Severity.Warning,
                        $"`{prediction[index].ActiveMachines}` active machines, outside of expected range [`{lookbackMin}`, `{lookbackMax}`]",
                        stamp,
                        eventTimeUtc: prediction[index].PreciseTimeStamp);
                });
            }
        }
    }
}
