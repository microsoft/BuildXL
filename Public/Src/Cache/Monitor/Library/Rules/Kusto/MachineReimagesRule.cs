// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.Monitor.App.Rules;
using BuildXL.Cache.Monitor.App.Scheduling;
using Kusto.Data.Common;
using System.Text.Json;
using static BuildXL.Cache.Monitor.App.Analysis.Utilities;

namespace BuildXL.Cache.Monitor.Library.Rules.Kusto
{
    internal class MachineReimagesRule : MultipleStampRuleBase
    {
        public class Configuration : MultiStampRuleConfiguration
        {
            public Configuration(MultiStampRuleConfiguration kustoRuleConfiguration)
                : base(kustoRuleConfiguration)
            {
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromHours(1);

            public int MaximumExamplesOnAlert { get; set; } = 5;

            public Thresholds<double> ReimageRateThresholds = new Thresholds<double>()
            {
                Info = 0,
                Warning = 0.01,
                Error = 0.1,
                Fatal = 0.2,
            };
        }

        private readonly Configuration _configuration;

        /// <inheritdoc />
        public override string Identifier => $"{nameof(MachineReimagesRule)}:{_configuration.Environment}";

        public MachineReimagesRule(Configuration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

#pragma warning disable CS0649
        internal class Result
        {
            public string Stamp = string.Empty;
            public ulong Total;
            public ulong Reimaged;
            public string Machines = string.Empty;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            var query =
                $@"
                let end = now();
                let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                table('{_configuration.CacheTableName}')
                | where PreciseTimeStamp between (start .. end)
                | summarize Total=dcount(Machine), Reimaged=dcountif(Machine, Message has 'MemoryContentDirectory starting with 0 entries from no file'), Machines=make_set_if(Machine, Message has 'MemoryContentDirectory starting with 0 entries from no file') by Stamp
                | extend Machines=tostring(Machines)
                | where not(isnull(Total))";
            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

            GroupByStampAndCallHelper(results, result => result.Stamp, rule);

            void rule(string stamp, List<Result> results)
            {
                if (results.Count == 0)
                {
                    return;
                }

                var result = results.First();

                var reimageRate = result.Reimaged / (double)result.Total;
                _configuration.ReimageRateThresholds.Check(
                    reimageRate,
                    (severity, cut) =>
                    {
                        var examples = string.Empty;
                        if (!string.IsNullOrEmpty(result.Machines))
                        {
                            var machines = JsonSerializer.Deserialize<List<string>>(result.Machines)!;
                            examples = ". Examples: " + string.Join(", ", machines.Take(_configuration.MaximumExamplesOnAlert).Select(m => $"`{m}`"));
                        }

                        Emit(
                            context,
                            "ReimageRate",
                            severity,
                            $"Detected `{Math.Round(reimageRate * 100.0, 4, MidpointRounding.AwayFromZero)}%` ({result.Reimaged} / {result.Total}) of the stamp has been reimaged in over the last `{_configuration.LookbackPeriod}` period{examples}",
                            stamp);
                    });
            }
        }
    }
}
