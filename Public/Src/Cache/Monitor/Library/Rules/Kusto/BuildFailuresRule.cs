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
    internal class BuildFailuresRule : MultipleStampRuleBase
    {
        public class Configuration : MultiStampRuleConfiguration
        {
            public Configuration(MultiStampRuleConfiguration kustoRuleConfiguration)
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

            public IcmThresholds<double> FailureRateIcmThresholds = new IcmThresholds<double>()
            {
                Sev4 = 0.5,
            };

            /// <summary>
            /// Avoid the case where we fail 1 out of 1 builds
            /// </summary>
            public long MinimumAmountOfBuildsForIcm { get; set; } = 5;

            public TimeSpan IcmIncidentCacheTtl { get; set; } = TimeSpan.FromHours(1);
        }

        private readonly Configuration _configuration;

        /// <inheritdoc />
        public override string Identifier => $"{nameof(BuildFailuresRule)}:{_configuration.Environment}";

        public BuildFailuresRule(Configuration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

#pragma warning disable CS0649
        internal class Result
        {
            public long Total;
            public long Failed;
            public double FailureRate;
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
                CacheInvocationsWithErrors('', start, end)
                | summarize Total=count(), Failed=countif(CacheImplicated) by Stamp
                | extend FailureRate=(toreal(Failed)/toreal(Total))
                | where not(isnull(Failed))";
            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

            await GroupByStampAndCallHelperAsync<Result>(results, result => result.Stamp, buildFailuresHelper);

            async Task buildFailuresHelper(string stamp, List<Result> results)
            {
                if (results.Count == 0)
                {
                    Emit(context, "NoBuilds", Severity.Info,
                        $"Stamp hasn't completed any builds in at least `{_configuration.LookbackPeriod}`",
                        stamp,
                        eventTimeUtc: now);
                    return;
                }

                var failureRate = results[0].FailureRate;
                var failed = results[0].Failed;
                var total = results[0].Total;
                _configuration.FailureRateThresholds.Check(failureRate, (severity, threshold) =>
                {
                    Emit(context, "FailureRate", severity,
                        $"Build failure rate `{failed}/{total}={Math.Round(failureRate * 100.0, 4, MidpointRounding.AwayFromZero)}%` over last `{_configuration.LookbackPeriod}``",
                        stamp,
                        eventTimeUtc: now);
                });

                if (total >= _configuration.MinimumAmountOfBuildsForIcm)
                {
                    await _configuration.FailureRateIcmThresholds.CheckAsync(failureRate, (severity, threshold) =>
                    {
                        return EmitIcmAsync(
                            severity,
                            title: $"{stamp}: build failure rate is higher than {threshold*100}%",
                            stamp,
                            machines: null,
                            correlationIds: null,
                            description: $"Build failure rate `{failed}/{total}={Math.Round(failureRate * 100.0, 4, MidpointRounding.AwayFromZero)}%` over last `{_configuration.LookbackPeriod}``",
                            eventTimeUtc: now,
                            cacheTimeToLive: _configuration.IcmIncidentCacheTtl);
                    });
                }
            }
        }
    }
}
