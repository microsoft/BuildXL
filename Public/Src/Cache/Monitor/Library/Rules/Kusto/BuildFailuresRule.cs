// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Scheduling;
using Kusto.Data.Common;
using static BuildXL.Cache.Monitor.App.Analysis.Utilities;

namespace BuildXL.Cache.Monitor.App.Rules.Kusto
{
    internal class BuildFailuresRule : KustoRuleBase
    {
        public class Configuration : KustoRuleConfiguration
        {
            public Configuration(KustoRuleConfiguration kustoRuleConfiguration)
                : base(kustoRuleConfiguration)
            {
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromHours(1);

            public Thresholds<double> FailureRateThresholds = new Thresholds<double>()
            {
                Info = 0.1,
                Warning = 0.2,
                Error = 0.4,
                Fatal = 0.5,
            };
        }

        private readonly Configuration _configuration;

        public override string Identifier => $"{nameof(BuildFailuresRule)}:{_configuration.StampId}";

        public BuildFailuresRule(Configuration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

#pragma warning disable CS0649
        private class Result
        {
            public long Total;
            public long Failed;
            public double FailureRate;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            var now = _configuration.Clock.UtcNow;
            var query =
                $@"
                let end = now();
                let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                CacheInvocationsWithErrors(""{_configuration.Stamp}"", start, end)
                | summarize Total=count(), Failed=countif(CacheImplicated)
                | extend FailureRate=(toreal(Failed)/toreal(Total))
                | where not(isnull(Failed))";
            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

            if (results.Count == 0)
            {
                Emit(context, "NoBuilds", Severity.Info,
                    $"Stamp hasn't completed any builds in at least `{_configuration.LookbackPeriod}`",
                    eventTimeUtc: now);
                return;
            }

            var failureRate = results[0].FailureRate;
            _configuration.FailureRateThresholds.Check(failureRate, (severity, threshold) =>
            {
                Emit(context, "FailureRate", severity,
                    $"Build failure rate `{Math.Round(failureRate * 100.0, 4, MidpointRounding.AwayFromZero)}%` over last `{_configuration.LookbackPeriod}``",
                    eventTimeUtc: now);
            });
        }
    }
}
