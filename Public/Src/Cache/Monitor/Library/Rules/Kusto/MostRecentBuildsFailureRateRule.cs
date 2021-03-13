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
    internal class MostRecentBuildsFailureRateRule : MultipleStampRuleBase
    {
        public class Configuration : MultiStampRuleConfiguration
        {
            public Configuration(MultiStampRuleConfiguration kustoRuleConfiguration)
                : base(kustoRuleConfiguration)
            {
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromDays(7);

            public Thresholds<double> FailureRateThresholds = new Thresholds<double>()
            {
                Info = 0.1,
                Warning = 0.2,
                Error = 0.4,
                Fatal = 0.5,
            };

            public IcmThresholds<double> FailureRateIcmThresholds = new IcmThresholds<double>()
            {
                Sev4 = 0.1,
                Sev3 = 0.25,
                Sev2 = 0.5,
            };

            public long AmountOfBuilds { get; set; } = 100;

            public TimeSpan IcmIncidentCacheTtl { get; set; } = TimeSpan.FromHours(1);
        }

        private readonly Configuration _configuration;

        /// <inheritdoc />
        public override string Identifier => $"{nameof(MostRecentBuildsFailureRateRule)}:{_configuration.Environment}";

        public MostRecentBuildsFailureRateRule(Configuration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

#pragma warning disable CS0649
        internal class Result
        {
            public long Failed;
            public string Stamp = string.Empty;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            var now = _configuration.Clock.UtcNow;
            var query =
                $@"
                let lookbackBuilds = {_configuration.AmountOfBuilds};
                let lookbackTime = {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                let Builds = materialize(BuildInfo
                | where PreciseTimeStamp between (ago(lookbackTime) .. now())
                | where InProbation != 1
                | partition by OwnerStampId (top lookbackBuilds by EndTime));
                let times = materialize(
                    Builds
                    | summarize TotalBuilds = count(), Start = min(StartTime), End = max(StartTime) by OwnerStampId
                    | where TotalBuilds == lookbackBuilds);
                let start = toscalar(times| summarize min(Start));
                let end = toscalar(times| summarize max(End));
                let failures = CacheInvocationsWithErrors('', start, end) | where CacheImplicated;
                Builds | lookup kind=inner failures on $left.BuildId == $right.BuildId
                | summarize Failed = count() by Stamp = OwnerStampId";
            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

            await GroupByStampAndCallHelperAsync<Result>(results, result => result.Stamp, buildFailuresHelper);

            async Task buildFailuresHelper(string stamp, List<Result> results)
            {
                if (results.Count == 0)
                {
                    return;
                }

                var failed = results[0].Failed;
                var failureRate = failed * 1.0 / _configuration.AmountOfBuilds;
                _configuration.FailureRateThresholds.Check(failureRate, (severity, threshold) =>
                {
                    Emit(context, "FailureRate", severity,
                        $"Build failure rate {Math.Round(failureRate * 100.0, 4, MidpointRounding.AwayFromZero)}%` over last `{_configuration.AmountOfBuilds} builds.``",
                        stamp,
                        eventTimeUtc: now);
                });

                await _configuration.FailureRateIcmThresholds.CheckAsync(failureRate, (severity, threshold) =>
                {
                    return EmitIcmAsync(
                        severity,
                        title: $"{stamp}: build failure rate over last {_configuration.AmountOfBuilds} builds is higher than {threshold*100}%",
                        stamp,
                        machines: null,
                        correlationIds: null,
                        description: $"Build failure rate `{failed}/{_configuration.AmountOfBuilds}={Math.Round(failureRate * 100.0, 4, MidpointRounding.AwayFromZero)}%` over last `{_configuration.AmountOfBuilds} builds``",
                        eventTimeUtc: now,
                        cacheTimeToLive: _configuration.IcmIncidentCacheTtl);
                });
            }
        }
    }
}
