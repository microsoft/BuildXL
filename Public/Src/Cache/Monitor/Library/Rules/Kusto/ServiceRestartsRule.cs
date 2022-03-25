// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.Monitor.App.Scheduling;
using BuildXL.Cache.Monitor.Library.Rules;
using Kusto.Data.Common;
using static BuildXL.Cache.Monitor.App.Analysis.Utilities;

namespace BuildXL.Cache.Monitor.App.Rules.Kusto
{
    internal class ServiceRestartsRule : MultipleStampRuleBase
    {
        public class Configuration : MultiStampRuleConfiguration
        {
            public Configuration(MultiStampRuleConfiguration kustoRuleConfiguration)
                : base(kustoRuleConfiguration)
            {
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromHours(1);

            public Thresholds<long> MachinesThresholds { get; set; } = new Thresholds<long>()
            {
                Info = 1,
                Warning = 3,
                Error = 5,
            };

            public Thresholds<long> ExceptionsThresholds { get; set; } = new Thresholds<long>()
            {
                Info = 1,
                Warning = 5,
                Error = 10,
            };
        }

        private readonly Configuration _configuration;

        /// <inheritdoc />
        public override string Identifier => $"{nameof(ServiceRestartsRule)}:{_configuration.Environment}";

        public ServiceRestartsRule(Configuration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

#pragma warning disable CS0649
        internal class Result
        {
            public string Machine = string.Empty;
            public string ExceptionType = string.Empty;
            public long Total;
            public DateTime LatestTimeUtc;
            public DateTime EarliestTimeUtc;
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
                let Exceptions = CloudCacheLogEvent
                | where PreciseTimeStamp between (start .. end)
                | where Message has 'Unhandled Exception in service.'
                | project PreciseTimeStamp, Stamp, Machine, Service, Exception
                | extend KnownFormat=(Exception has 'Error=[' and Exception has 'Diagnostics=[');
                let FormattedExceptions = Exceptions
                | where KnownFormat
                | parse Exception with ExceptionType:string ': Error=[' Error:string '] Diagnostics=[' Diagnostics:string
                | parse Diagnostics with ExceptionTypeFromDiagnostics:string ': ' *
                | extend ExceptionType = iif(isnull(ExceptionTypeFromDiagnostics), ExceptionType, ExceptionTypeFromDiagnostics)
                | project-away ExceptionTypeFromDiagnostics
                | project-away Exception;
                let UnknownExceptions = Exceptions
                | where not(KnownFormat)
                | parse Exception with ExceptionType:string ': ' *
                | project-rename Diagnostics=Exception;
                FormattedExceptions
                | union UnknownExceptions
                | extend ExceptionType=iif(isempty(ExceptionType) or isnull(ExceptionType), 'Unknown', ExceptionType)
                | extend ExceptionType=iif(ExceptionType has ' ', extract('([A-Za-z0-9.]+) .*', 1, ExceptionType), ExceptionType)
                | summarize Total=count(), LatestTimeUtc=max(PreciseTimeStamp), EarliestTimeUtc=min(PreciseTimeStamp) by Machine, ExceptionType, Stamp
                | where not(isnull(Total));";
            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

            GroupByStampAndCallHelper<Result>(results, result => result.Stamp, serviceRestartsHelper);

            void serviceRestartsHelper(string stamp, List<Result> results)
            {
                if (results.Count == 0)
                {
                    return;
                }

                foreach (var group in results.GroupBy(result => result.ExceptionType))
                {
                    var numberOfMachines = group.Select(result => result.Machine).Distinct().Count();
                    var numberOfExceptions = group.Sum(result => result.Total);

                    var minTimeUtc = group.Min(result => result.EarliestTimeUtc);
                    var eventTimeUtc = group.Max(result => result.LatestTimeUtc);

                    BiThreshold(numberOfMachines, numberOfExceptions, _configuration.MachinesThresholds, _configuration.ExceptionsThresholds, (severity, machinesThreshold, exceptionsThreshold) =>
                    {
                        var examples = string.Join(".\r\n", group
                            .Select(result => $"\t`{result.Machine}` had `{result.Total}` restart(s) between `{result.EarliestTimeUtc}` and `{result.LatestTimeUtc}` UTC"));
                        Emit(context, $"ServiceRestarts_{group.Key}", severity,
                            $"`{numberOfMachines}` machine(s) had `{numberOfExceptions}` unexpected service restart(s) due to exception `{group.Key}` since `{now - minTimeUtc}` ago. Examples:\r\n{examples}",
                            stamp,
                            $"`{numberOfMachines}` machine(s) had `{numberOfExceptions}` unexpected service restart(s) due to exception `{group.Key}` since `{now - minTimeUtc}` ago",
                            eventTimeUtc: eventTimeUtc);
                    });
                }
            }
        }
    }
}
