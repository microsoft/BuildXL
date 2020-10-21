// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Scheduling;
using BuildXL.Cache.Monitor.Library.Rules;
using Kusto.Data.Common;
using static BuildXL.Cache.Monitor.App.Analysis.Utilities;

namespace BuildXL.Cache.Monitor.App.Rules.Kusto
{
    internal class LastProducedCheckpointRule : MultipleStampRuleBase
    {
        public class Configuration : MultiStampRuleConfiguration
        {
            public Configuration(MultiStampRuleConfiguration kustoRuleConfiguration)
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

        /// <inheritdoc />
        public override string Identifier => $"{nameof(LastProducedCheckpointRule)}:{_configuration.Environment}";

        public LastProducedCheckpointRule(Configuration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

#pragma warning disable CS0649
        internal class Result
        {
            public DateTime PreciseTimeStamp;
            public TimeSpan Age;
            public string Machine = string.Empty;
            public string Stamp = string.Empty;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            var now = _configuration.Clock.UtcNow;
            var query =
                $@"
                let end = now();
                let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                table('{_configuration.CacheTableName}')
                | where PreciseTimeStamp between (start .. end)
                | where Role == 'Master'
                | where Operation == 'CreateCheckpointAsync' and isnotempty(Duration)
                | where Result == '{Constants.ResultCode.Success}'
                | summarize (PreciseTimeStamp, Machine)=arg_max(PreciseTimeStamp, Machine) by Stamp
                | extend Age = end - PreciseTimeStamp
                | where not(isnull(PreciseTimeStamp))";
            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

            GroupByStampAndCallHelper<Result>(results, result => result.Stamp, lastProducedCheckpointHelper);

            void lastProducedCheckpointHelper(string stamp, List<Result> results)
            {
                if (results.Count == 0)
                {
                    Emit(context, "NoLogs", Severity.Fatal,
                        $"No checkpoints produced for at least {_configuration.LookbackPeriod}",
                        stamp,
                        eventTimeUtc: now);
                    return;
                }

                var age = results[0].Age;
                _configuration.AgeThresholds.Check(age, (severity, threshold) =>
                {
                    Emit(context, "CreationThreshold", severity,
                        $"Newest checkpoint age `{age}` above threshold `{threshold}`. Master is {results[0].Machine}",
                        stamp,
                        eventTimeUtc: results[0].PreciseTimeStamp);
                });
            }

        }
    }
}
