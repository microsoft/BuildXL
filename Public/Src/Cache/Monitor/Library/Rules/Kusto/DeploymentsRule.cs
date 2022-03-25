// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Rules;
using BuildXL.Cache.Monitor.App.Scheduling;
using Kusto.Data.Common;

namespace BuildXL.Cache.Monitor.Library.Rules.Kusto
{
    internal class DeploymentsRule : MultipleStampRuleBase
    {
        public class Configuration : MultiStampRuleConfiguration
        {
            public Configuration(MultiStampRuleConfiguration other) : base(other)
            {
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromHours(12);

            public TimeSpan AlertPeriod { get; set; } = TimeSpan.FromMinutes(10);
        }

        public DeploymentsRule(Configuration configuration) : base(configuration)
        {
            Contract.Requires(configuration.AlertPeriod < configuration.LookbackPeriod);

            _configuration = configuration;
        }

        /// <inheritdoc />
        public override string Identifier => $"{nameof(DeploymentsRule)}:{_configuration.Environment}";

        private readonly Configuration _configuration;

        public override Task Run(RuleContext context)
        {
            var now = _configuration.Clock.UtcNow;

            // These are all pretty much the same. Not generalizing on purpose
            var cacheConfigCheck = CheckConfigurationDeployments(context, now);
            // These two monitor kind of the same thing. However, stamps with launcher enabled may change the cache
            // version without changing the service version
            var cacheVersionCheck = CheckCacheVersionDeployments(context, now);
            var serviceVersionCheck = CheckServiceVersionDeployments(context, now);

            return Task.WhenAll(cacheConfigCheck, cacheVersionCheck, serviceVersionCheck);
        }

#pragma warning disable CS0649
        internal record ConfigurationCheckResult
        {
            public string Stamp = string.Empty;
            public string ConfigurationId = string.Empty;
        }
#pragma warning restore CS0649

        private async Task CheckConfigurationDeployments(RuleContext context, DateTime now)
        {
            var query = $@"
                let end = now(); let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                CloudCacheLogEvent
                | where PreciseTimeStamp between (start .. end)
                | where Operation == 'StartupAsync' // Filtering to avoid having too much data
                | summarize FirstSeen=min(PreciseTimeStamp) by Stamp, ConfigurationId
                | where ConfigurationId startswith 'GIT:'
                | extend ConfigurationId = substring(ConfigurationId, 4)
                | where FirstSeen > {CslTimeSpanLiteral.AsCslString(_configuration.AlertPeriod)}
                | project-away FirstSeen
                | sort by Stamp asc";

            var results = (await QueryKustoAsync<ConfigurationCheckResult>(context, query)).ToList();

            GroupByStampAndCallHelper(results, result => result.Stamp, helper);

            void helper(string stamp, List<ConfigurationCheckResult> results)
            {
                if (results.Count == 0)
                {
                    return;
                }

                foreach (var result in results)
                {
                    Emit(context, "ConfigurationDeployment", Severity.Info,
                        $"Detected deployment of configuration with commit ID `{result.ConfigurationId}`",
                        stamp,
                        eventTimeUtc: now);
                }
            }
        }

#pragma warning disable CS0649
        internal record CacheVersionCheckResult
        {
            public string Stamp = string.Empty;
            public string CacheVersion = string.Empty;
        }
#pragma warning restore CS0649

        private async Task CheckCacheVersionDeployments(RuleContext context, DateTime now)
        {
            var query = $@"
                let end = now(); let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                CloudCacheLogEvent
                | where PreciseTimeStamp between (start .. end)
                | where Operation == 'StartupAsync' // Filtering to avoid having too much data
                | summarize FirstSeen=min(PreciseTimeStamp) by Stamp, CacheVersion
                | where FirstSeen > {CslTimeSpanLiteral.AsCslString(_configuration.AlertPeriod)}
                | project-away FirstSeen
                | sort by Stamp asc";

            var results = (await QueryKustoAsync<CacheVersionCheckResult>(context, query)).ToList();

            GroupByStampAndCallHelper<CacheVersionCheckResult>(results, result => result.Stamp, helper);

            void helper(string stamp, List<CacheVersionCheckResult> results)
            {
                if (results.Count == 0)
                {
                    return;
                }

                foreach (var result in results)
                {
                    Emit(context, "CacheVersionDeployment", Severity.Info,
                        $"Detected deployment of new cache version with date `{result.CacheVersion}`",
                        stamp,
                        eventTimeUtc: now);
                }
            }
        }

#pragma warning disable CS0649
        internal record ServiceVersionCheckResult
        {
            public string Stamp = string.Empty;
            public string ServiceVersion = string.Empty;
        }
#pragma warning restore CS0649

        private async Task CheckServiceVersionDeployments(RuleContext context, DateTime now)
        {
            var query = $@"
                let end = now(); let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                CloudCacheLogEvent
                | where PreciseTimeStamp between (start .. end)
                | where Operation == 'StartupAsync' // Filtering to avoid having too much data
                | summarize FirstSeen=min(PreciseTimeStamp) by Stamp, ServiceVersion
                | where FirstSeen > {CslTimeSpanLiteral.AsCslString(_configuration.AlertPeriod)}
                | project-away FirstSeen
                | sort by Stamp asc";

            var results = (await QueryKustoAsync<ServiceVersionCheckResult>(context, query)).ToList();

            GroupByStampAndCallHelper<ServiceVersionCheckResult>(results, result => result.Stamp, helper);

            void helper(string stamp, List<ServiceVersionCheckResult> results)
            {
                if (results.Count == 0)
                {
                    return;
                }

                foreach (var result in results)
                {
                    Emit(context, "ServiceVersionDeployment", Severity.Info,
                        $"Detected deployment of new service version with ID `{result.ServiceVersion}`",
                        stamp,
                        eventTimeUtc: now);
                }
            }
        }
    }
}
