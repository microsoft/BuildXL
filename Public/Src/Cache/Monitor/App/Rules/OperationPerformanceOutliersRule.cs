using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using Kusto.Data.Common;
using static BuildXL.Cache.Monitor.App.Utilities;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal class OperationPerformanceOutliersRule : KustoRuleBase
    {
        public class DynamicCheck
        {
            public TimeSpan LookbackPeriod { get; set; }

            public TimeSpan DetectionPeriod { get; set; }

            public string Match { get; set; }

            private string _name = null;

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

        public class Configuration : KustoRuleConfiguration
        {
            public Configuration(KustoRuleConfiguration kustoRuleConfiguration)
                : base(kustoRuleConfiguration)
            {
            }

            public DynamicCheck Check { get; set; }
        }

        private readonly Configuration _configuration;

        public override string Identifier => $"{nameof(OperationPerformanceOutliersRule)};{_configuration.Check.Name}:{_configuration.Environment}/{_configuration.Stamp}";

        public OperationPerformanceOutliersRule(Configuration configuration)
            : base(configuration)
        {
            Contract.RequiresNotNull(configuration);
            Contract.RequiresNotNull(configuration.Check);
            _configuration = configuration;
        }

#pragma warning disable CS0649
        private class Result
        {
            public DateTime PreciseTimeStamp;
            public string Machine;
            public string CorrelationId;
            public double TimeMs;
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
                let Events = materialize(table(""{_configuration.CacheTableName}"")
                | where PreciseTimeStamp between (start .. end)
                | where Stamp == ""{_configuration.Stamp}""
                | where Service == ""{Constants.ServiceName}"" or Service == ""{Constants.MasterServiceName}""
                | where Message has ""{_configuration.Check.Match} stop ""
                | where Message has ""result=[Success""
                | parse Message with OperationId:string "" "" * "" stop "" TimeMs:double ""ms. "" *
                | extend CorrelationId={(_configuration.CacheTableName == Constants.NewTableName ? "CorrelationId" : "OperationId")}
                | project PreciseTimeStamp, Machine, CorrelationId, TimeMs);
                let DetectionEvents = Events
                | where PreciseTimeStamp between (detection .. end);
                let DescriptiveStatistics = Events
                | where PreciseTimeStamp between (start .. detection){perhapsLookbackFilter}
                | summarize hint.shufflekey=Machine hint.num_partitions=64 (P50, P95)=percentiles(TimeMs, 50, 95) by Machine
                | where not(isnull(Machine));
                DescriptiveStatistics
                | join kind=rightouter hint.strategy=broadcast hint.num_partitions=64 DetectionEvents on Machine
                | where {_configuration.Check.Constraint ?? "true"}
                | project PreciseTimeStamp, Machine, CorrelationId, TimeMs
                | where not(isnull(Machine));";
            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

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

            BiThreshold(numberOfMachines, numberOfFailures, _configuration.Check.MachineThresholds, _configuration.Check.FailureThresholds, (severity, machineThreshold, failureThreshold) => {
                var examples = string.Join(".\r\n", reports
                    .Select(entry =>
                    {
                        var failures = string.Join(", ", entry.Operations
                            .Take(5)
                            .Select(f => $"`{f.CorrelationId}` (`{TimeSpan.FromMilliseconds(f.TimeMs)}`)"));

                        return $"`{entry.Machine}` (total `{entry.Operations.Count}`): {failures}";
                    }));
                var summaryExamples = string.Join(".\r\n", reports
                    .Take(3)
                    .Select(entry =>
                    {
                        var failures = string.Join(", ", entry.Operations
                            .Take(2)
                            .Select(f => $"`{f.CorrelationId}` (`{TimeSpan.FromMilliseconds(f.TimeMs)}`)"));

                        return $"\t`{entry.Machine}` (total `{entry.Operations.Count}`): {failures}";
                    }));

                Emit(context, $"Performance_{_configuration.Check.Name}", severity,
                    $"Operation `{_configuration.Check.Name}` has taken too long `{numberOfFailures}` time(s) across `{numberOfMachines}` machine(s) in the last `{_configuration.Check.DetectionPeriod}`. Examples:\r\n{examples}",
                    $"Operation `{_configuration.Check.Name}` has taken too long `{numberOfFailures}` time(s) across `{numberOfMachines}` machine(s) in the last `{_configuration.Check.DetectionPeriod}`. Examples:\r\n{summaryExamples}",
                    eventTimeUtc: eventTimeUtc);
            });
        }
    }
}
