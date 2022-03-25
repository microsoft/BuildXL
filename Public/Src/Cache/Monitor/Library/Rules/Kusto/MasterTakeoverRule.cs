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
    internal class MasterTakeoverRule : MultipleStampRuleBase
    {
        public class Configuration : MultiStampRuleConfiguration
        {
            public Configuration(MultiStampRuleConfiguration other) : base(other)
            {
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromMinutes(10);
        }

        public MasterTakeoverRule(Configuration configuration) : base(configuration)
        {
            _configuration = configuration;
        }

        /// <inheritdoc />
        public override string Identifier => $"{nameof(MasterTakeoverRule)}:{_configuration.Environment}";

        private readonly Configuration _configuration;

#pragma warning disable CS0649
        internal record Result
        {
            public DateTime PreciseTimeStamp;
            public string Stamp = string.Empty;
            public string Machine = string.Empty;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            var now = _configuration.Clock.UtcNow;
            var query =
                $@"
                let end = now(); let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                CloudCacheLogEvent
                | where PreciseTimeStamp between (start .. end)
                | where MachineFunction == 'GlobalCache'
                | where Component == 'LocalLocationStore' and Operation == 'ProcessStateAsync'
                | where Message has 'Switching roles'
                | where Message has 'New=Master'
                | project PreciseTimeStamp, Stamp, Machine
                | sort by Stamp asc, PreciseTimeStamp asc";

            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

            GroupByStampAndCallHelper<Result>(results, result => result.Stamp, helper);

            void helper(string stamp, List<Result> results)
            {
                foreach (var result in results.OrderBy(r => r.PreciseTimeStamp))
                {
                    Emit(context,
                         "MasterTakeover",
                         Severity.Info,
                         $"Detected master takeover by machine `{result.Machine}`",
                         stamp: result.Stamp,
                         eventTimeUtc: result.PreciseTimeStamp);
                }
            }
        }
    }
}
