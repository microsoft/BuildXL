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
    internal class OperationPerformanceOutliersRule : MultipleStampRuleBase
    {
        public class DynamicCheck
        {
            public TimeSpan LookbackPeriod { get; set; }

            public TimeSpan DetectionPeriod { get; set; }

            public string Match { get; set; } = string.Empty;

            private string? _name = null;

            public string Name
            {
                get => _name ?? Match;
                set => _name = value;
            }

            /// <summary>
            /// Helps query performance in very high frequency scenarios
            /// </summary>
            public long? MaximumLookbackOperations { get; set; }

            /// <summary>
            /// Number of machines to produce different levels of alerts
            /// </summary>
            public Thresholds<long> MachineThresholds { get; set; } = new Thresholds<long>()
            {
                Info = 1,
                Warning = 5,
                Error = 20,
            };

            /// <summary>
            /// Number of failed operations to produce different levels of alerts
            /// </summary>
            public Thresholds<long> FailureThresholds { get; set; } = new Thresholds<long>()
            {
                Info = 10,
                Warning = 50,
                Error = 1000,
            };

            /// <summary>
            /// Must be a valid KQL string that can be put right after a
            /// 
            ///  | where {string here}
            ///
            /// Certain descriptive statistics are provided. See the actual query for more information.
            /// </summary>
            /// <remarks>
            /// The constraint check and statistics provided are on a per-machine basis.
            /// </remarks>
            public string Constraint { get; set; } = "true";
        }

        public class Configuration : MultiStampRuleConfiguration
        {
            public Configuration(MultiStampRuleConfiguration kustoRuleConfiguration, DynamicCheck check)
                : base(kustoRuleConfiguration)
            {
                Check = check;
            }

            public DynamicCheck Check { get; }
        }

        private readonly Configuration _configuration;

        /// <inheritdoc />
        public override string Identifier => $"{nameof(OperationPerformanceOutliersRule)};{_configuration.Check.Name}:{_configuration.Environment}";

        public OperationPerformanceOutliersRule(Configuration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

#pragma warning disable CS0649
        internal class Result
        {
            public DateTime PreciseTimeStamp;
            public string Machine = string.Empty;
            public string CorrelationId = string.Empty;
            public TimeSpan Duration;
            public string Stamp = string.Empty;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            var now = _configuration.Clock.UtcNow;

            var perhapsLookbackFilter = _configuration.Check.MaximumLookbackOperations is null ? "" : $"\n| sample {CslLongLiteral.AsCslString(_configuration.Check.MaximumLookbackOperations)}\n";
            var query =
                $@"
                let end = now();
                let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.Check.LookbackPeriod)};
                let detection = end - {CslTimeSpanLiteral.AsCslString(_configuration.Check.DetectionPeriod)};
                let Events = materialize(CloudCacheLogEvent
                | where PreciseTimeStamp between (start .. end)
                | where Message has '{_configuration.Check.Match}' and isnotempty(Duration)
                | where Result != '{Constants.ResultCode.Success}'
                | project PreciseTimeStamp, Machine, CorrelationId, Duration, Stamp);
                let DetectionEvents = Events
                | where PreciseTimeStamp between (detection .. end);
                let DescriptiveStatistics = Events
                | where PreciseTimeStamp between (start .. detection){perhapsLookbackFilter}
                | summarize hint.shufflekey=Machine hint.num_partitions=64 (P50, P95)=percentiles(Duration, 50, 95) by Machine, Stamp
                | where not(isnull(Machine));
                DescriptiveStatistics
                | join kind=rightouter hint.strategy=broadcast hint.num_partitions=64 DetectionEvents on Machine
                | where {_configuration.Check.Constraint ?? "true"}
                | extend Machine = iif(isnull(Machine) or isempty(Machine), Machine1, Machine)
                | extend Stamp = iif(isnull(Stamp) or isempty(Stamp), Stamp1, Stamp)
                | project PreciseTimeStamp, Machine, CorrelationId, Duration, Stamp
                | where not(isnull(Machine));";
            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

            GroupByStampAndCallHelper<Result>(results, result => result.Stamp, operationPerformanceOutliersHelper);

            void operationPerformanceOutliersHelper(string stamp, List<Result> results)
            {
                if (results.Count == 0)
                {
                    return;
                }

                var reports = results
                    .AsParallel()
                    .GroupBy(result => result.Machine)
                    .Select(group => (
                        Machine: group.Key,
                        Operations: group
                            .OrderByDescending(result => result.PreciseTimeStamp)
                            .ToList()))
                    .OrderByDescending(entry => entry.Operations.Count)
                    .ToList();

                var eventTimeUtc = reports.Max(report => report.Operations[0].PreciseTimeStamp);

                var numberOfMachines = reports.Count;
                var numberOfFailures = reports.Sum(entry => entry.Operations.Count);

                BiThreshold(numberOfMachines, numberOfFailures, _configuration.Check.MachineThresholds, _configuration.Check.FailureThresholds, (severity, machineThreshold, failureThreshold) =>
                {
                    var examples = string.Join(".\r\n", reports
                        .Select(entry =>
                        {
                            var failures = string.Join(", ", entry.Operations
                                .Take(5)
                                .Select(f => $"`{f.CorrelationId}` (`{f.Duration}`)"));

                            return $"`{entry.Machine}` (total `{entry.Operations.Count}`): {failures}";
                        }));
                    var summaryExamples = string.Join(".\r\n", reports
                        .Take(3)
                        .Select(entry =>
                        {
                            var failures = string.Join(", ", entry.Operations
                                .Take(2)
                                .Select(f => $"`{f.CorrelationId}` (`{f.Duration}`)"));

                            return $"\t`{entry.Machine}` (total `{entry.Operations.Count}`): {failures}";
                        }));

                    Emit(context, $"Performance_{_configuration.Check.Name}", severity,
                        $"Operation `{_configuration.Check.Name}` has taken too long `{numberOfFailures}` time(s) across `{numberOfMachines}` machine(s) in the last `{_configuration.Check.DetectionPeriod}`. Examples:\r\n{examples}",
                        stamp,
                        $"Operation `{_configuration.Check.Name}` has taken too long `{numberOfFailures}` time(s) across `{numberOfMachines}` machine(s) in the last `{_configuration.Check.DetectionPeriod}`. Examples:\r\n{summaryExamples}",
                        eventTimeUtc: eventTimeUtc);
                });
            }
        }
    }
}
