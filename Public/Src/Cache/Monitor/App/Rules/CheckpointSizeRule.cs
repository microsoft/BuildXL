using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Monitor.App.Analysis;
using Kusto.Data.Common;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal class CheckpointSizeRule : KustoRuleBase
    {
        public class Configuration : KustoRuleConfiguration
        {
            public Configuration(KustoRuleConfiguration kustoRuleConfiguration)
                : base(kustoRuleConfiguration)
            {
            }

            /// <summary>
            /// Time to take for analysis
            /// </summary>
            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromDays(1);

            /// <summary>
            /// Time to take for detection
            /// </summary>
            public TimeSpan AnomalyDetectionHorizon { get; set; } = TimeSpan.FromHours(1);

            /// <summary>
            /// Maximum amount that a checkpoint can grow or decrease in size with respect to previous one
            /// </summary>
            public double MaximumPercentualDifference { get; set; } = 0.5;

            /// <summary>
            /// Minimum valid size. A checkpoint smaller than this triggers a fatal error.
            /// </summary>
            public string MinimumValidSize { get; set; } = "1KB";

            public long MinimumValidSizeBytes => MinimumValidSize.ToSize();

            /// <summary>
            /// Maximum valid size. A checkpoint larger than this triggers a fatal error.
            /// </summary>
            public string MaximumValidSize { get; set; } = "70GB";

            public long MaximumValidSizeBytes => MaximumValidSize.ToSize();

            /// <summary>
            /// Minimum percentage of data needed to estimate the maximum/minimum ranges
            /// </summary>
            public double MinimumTrainingPercentOfData { get; set; } = 0.75;

            /// <summary>
            /// Maximum amount that a checkpoint can grow or decrease in size with respect to lookback period
            /// </summary>
            public double MaximumGrowthWrtToLookback { get; set; } = 0.2;
        }

        private readonly Configuration _configuration;

        public override string Identifier => $"{nameof(CheckpointSizeRule)}:{_configuration.Environment}/{_configuration.Stamp}";

        public CheckpointSizeRule(Configuration configuration)
            : base(configuration)
        {
            Contract.RequiresNotNull(configuration);
            _configuration = configuration;
        }

#pragma warning disable CS0649
        private class Result
        {
            public DateTime PreciseTimeStamp;
            public long TotalSize;
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
                | where Message has ""CreateCheckpointAsync stop""
                | project PreciseTimeStamp, Message
                | parse Message with * ""SizeMb=["" SizeMb:double ""]"" *
                | project PreciseTimeStamp, TotalSize=tolong(SizeMb * 1000000)
                | sort by PreciseTimeStamp asc";
            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

            if (results.Count == 0)
            {
                _configuration.Logger.Error($"No checkpoints have been produced for at least {_configuration.LookbackPeriod}");
                return;
            }

            var detectionHorizon = now - _configuration.AnomalyDetectionHorizon;

            results
                .Select(r => (double)r.TotalSize)
                .OverPercentualDifference(_configuration.MaximumPercentualDifference)
                .Where(evaluation => results[evaluation.Index].PreciseTimeStamp >= detectionHorizon)
                .PerformOnLast(index =>
                {
                    var result = results[index];
                    var previousResult = results[index - 1];
                    Emit(context, "SizeDerivative", Severity.Warning,
                        $"Checkpoint size went from `{previousResult.TotalSize.ToSizeExpression()}` to `{result.TotalSize.ToSizeExpression()}`, which is higher than the threshold of `{_configuration.MaximumPercentualDifference * 100.0}%`",
                        $"Checkpoint size went from `{previousResult.TotalSize.ToSizeExpression()}` to `{result.TotalSize.ToSizeExpression()}`",
                        eventTimeUtc: result.PreciseTimeStamp);
                });

            var training = new List<Result>();
            var prediction = new List<Result>();
            results.SplitBy(r => r.PreciseTimeStamp <= detectionHorizon, training, prediction);

            if (prediction.Count == 0)
            {
                _configuration.Logger.Error($"No checkpoints have been produced for at least {_configuration.AnomalyDetectionHorizon}");
                return;
            }

            prediction
                .Select(p => p.TotalSize)
                .NotInRange(_configuration.MinimumValidSizeBytes, _configuration.MaximumValidSizeBytes)
                .PerformOnLast(index => {
                    var result = prediction[index];
                    Emit(context, "SizeValidRange", Severity.Warning,
                        $"Checkpoint size `{result.TotalSize.ToSizeExpression()}` out of valid range [`{_configuration.MinimumValidSize}`, `{_configuration.MaximumValidSize}`]",
                        eventTimeUtc: result.PreciseTimeStamp);
                });

            if (training.Count < _configuration.MinimumTrainingPercentOfData * results.Count)
            {
                return;
            }

            var lookbackSizes = training.Select(r => r.TotalSize);
            var expectedMin = (long)Math.Floor((1 - _configuration.MaximumGrowthWrtToLookback) * lookbackSizes.Min());
            var expectedMax = (long)Math.Ceiling((1 + _configuration.MaximumGrowthWrtToLookback) * lookbackSizes.Max());
            prediction
                .Select(p => p.TotalSize)
                .NotInRange(expectedMin, expectedMax)
                .PerformOnLast(index => {
                    var result = prediction[index];
                    Emit(context, "SizeExpectedRange", Severity.Warning,
                        $"Checkpoint size `{result.TotalSize.ToSizeExpression()}` out of expected range [`{expectedMin.ToSizeExpression()}`, `{expectedMax.ToSizeExpression()}`]",
                        eventTimeUtc: result.PreciseTimeStamp);
                });
        }
    }
}
