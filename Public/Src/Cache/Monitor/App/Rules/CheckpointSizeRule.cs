using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Security.Permissions;
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
            public string MinimumValidSize { get; set; } = "200MB";

            public long MinimumValidSizeBytes => MinimumValidSize.ToSize();

            /// <summary>
            /// Maximum valid size. A checkpoint larger than this triggers a fatal error.
            /// </summary>
            public string MaximumValidSize { get; set; } = "30GB";

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

        public override async Task Run()
        {
            var ruleRunTimeUtc = _configuration.Clock.UtcNow;

            // NOTE(jubayard): When a summarize is run over an empty result set, Kusto produces a single (null) row,
            // which is why we need to filter it out.
            var query =
                $@"CloudBuildLogEvent
                   | where PreciseTimeStamp > ago({CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)})
                   | where Service == ""{Constants.MasterServiceName}""
                   | where Stamp == ""{_configuration.Stamp}""
                   | where Message has ""Touching blob"" or Message has ""Uploading blob""
                   | project PreciseTimeStamp, Machine, Message
                   | parse Message with Id "" "" * ""of size "" Size: long "" "" *
                   | summarize PreciseTimeStamp = min(PreciseTimeStamp), TotalSize = sum(Size) by Id
                   | project PreciseTimeStamp, TotalSize
                   | sort by PreciseTimeStamp asc
                   | where not(isnull(PreciseTimeStamp))";
            var results = (await QuerySingleResultSetAsync<Result>(query)).ToList();

            var now = _configuration.Clock.UtcNow;
            if (results.Count == 0)
            {
                _configuration.Logger.Error($"No checkpoints have been produced for at least {_configuration.LookbackPeriod}");
                return;
            }

            var detectionHorizon = now - _configuration.AnomalyDetectionHorizon;

            var variability = new CheckPercentualDifference<Result>(r => r.TotalSize, _configuration.MaximumPercentualDifference);
            variability.Check(results, (index, result) => {
                if (result.PreciseTimeStamp < detectionHorizon)
                {
                    return;
                }

                var previousResult = results[index - 1];
                Emit(Severity.Warning,
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

            var range = new CheckRange<long>(Comparer<long>.Default, _configuration.MinimumValidSizeBytes, _configuration.MaximumValidSizeBytes);
            range.Check(prediction.Select(r => r.TotalSize), (index, value) => {
                Emit(Severity.Fatal,
                    $"Checkpoint size is `{value.ToSizeExpression()}`, which is outside of the valid range [`{_configuration.MinimumValidSize}`, `{_configuration.MaximumValidSize}`]",
                    $"Checkpoint size out of range: `{value.ToSizeExpression()}`",
                    eventTimeUtc: prediction[index].PreciseTimeStamp);
            });

            if (training.Count < _configuration.MinimumTrainingPercentOfData * results.Count)
            {
                return;
            }

            var lookbackSizes = training.Select(r => r.TotalSize);
            double lookbackMin = (1 - _configuration.MaximumGrowthWrtToLookback) * lookbackSizes.Min();
            double lookbackMax = (1 + _configuration.MaximumGrowthWrtToLookback) * lookbackSizes.Max();
            var range2 = new CheckRange<double>(Comparer<double>.Default, lookbackMin, lookbackMax);
            range2.Check(prediction.Select(r => (double)r.TotalSize), (index, valueDouble) => {
                var value = (long)Math.Ceiling(valueDouble);

                Emit(Severity.Warning,
                    $"Checkpoint size is `{value.ToSizeExpression()}`, which is out of the expected range [`{lookbackMin}`, `{lookbackMax}`]",
                    $"Checkpoint size out of expected range: `{value.ToSizeExpression()}`",
                    eventTimeUtc: prediction[index].PreciseTimeStamp);
            });
        }
    }
}
