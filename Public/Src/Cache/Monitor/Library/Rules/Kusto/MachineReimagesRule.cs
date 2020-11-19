// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.Monitor.App.Rules;
using BuildXL.Cache.Monitor.App.Scheduling;
using Kusto.Data.Common;
using System.Text.Json;
using static BuildXL.Cache.Monitor.App.Analysis.Utilities;
using BuildXL.Cache.Monitor.App;

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
                Info = 0.01,
                Warning = 0.1,
                Error = 0.2,
                Fatal = 0.3,
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
            public string Service = string.Empty;
            public ulong Total;
            public ulong Reimaged;
            public string Machines = string.Empty;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            // There is a lot of drama here around whether the Launcher is running or not. This is because the launcher
            // launcher creates its own MemoryContentDirectory, so the only message that we can use to do this is used
            // for two different reasons.
            var query =
                $@"
                let end = now();
                let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                table('{_configuration.CacheTableName}')
                | where PreciseTimeStamp between (start .. end)
                | summarize Total=dcount(Machine), Reimaged=dcountif(Machine, Message has 'MemoryContentDirectory starting with 0 entries from no file'), Machines=make_set_if(Machine, Message has 'MemoryContentDirectory starting with 0 entries from no file') by Stamp, Service
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

                // We skip stamps with the Launcher because things get really weird. This should be removed in the near future.
                var isLauncherRunning = results.Any(r => r.Service.Equals(Constants.CacheService, StringComparison.InvariantCultureIgnoreCase));
                if (isLauncherRunning)
                {
                    return;
                }
                
                var result = results.Where(r => r.Service.Equals(Constants.ContentAddressableStoreService, StringComparison.InvariantCultureIgnoreCase)).First();
                if (result.Reimaged == 0)
                {
                    return;
                }

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
