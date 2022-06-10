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
    internal class KeySpaceRule : MultipleStampRuleBase
    {
        public class Configuration : MultiStampRuleConfiguration
        {
            public Configuration(MultiStampRuleConfiguration other) : base(other)
            {
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromDays(7);
        }

        public KeySpaceRule(Configuration configuration) : base(configuration)
        {
            _configuration = configuration;
        }

        /// <inheritdoc />
        public override string Identifier => $"{nameof(KeySpaceRule)}:{_configuration.Environment}";

        private readonly Configuration _configuration;

#pragma warning disable CS0649
        internal record Result
        {
            public string Stamp = string.Empty;
            public string MachineFunction = string.Empty;
            public string KeySpace = string.Empty;
            public int Dcount_Machine;
            public DateTime Max_PreciseTimeStamp; 

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
                | where Message has 'KeySpacePrefix'
                | parse Message with * 'Keyspace"": ""' KeySpace '"",' *
                | project PreciseTimeStamp, Stamp, Machine, MachineFunction, KeySpace
                | summarize arg_max(PreciseTimeStamp, KeySpace) by Stamp, MachineFunction, Machine
                | summarize dcount(Machine), max(PreciseTimeStamp) by Stamp, MachineFunction, KeySpace
                | project Stamp, MachineFunction, KeySpace, Dcount_Machine = dcount_Machine, Max_PreciseTimeStamp = max_PreciseTimeStamp
                | sort by Stamp asc, MachineFunction";

            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

            foreach (var groupByStamp in results.GroupBy(result => result.Stamp))
            {
                foreach (var groupByKeySpace in groupByStamp.GroupBy(result => result.KeySpace))
                {
                    int globalCacheCount = 0;
                    int cmdAgentCount = 0;
                    foreach (var row in groupByKeySpace)
                    {
                        // has properties MachineFunction, Dcount_Machine,Max_PrecisetTimeStamp
                        if (row.MachineFunction == "GlobalCache")
                        {
                            globalCacheCount = row.Dcount_Machine;
                        }
                        if (row.MachineFunction == "CmdAgent")
                        {
                            cmdAgentCount = row.Dcount_Machine;
                        }
                    }

                    if (globalCacheCount == 0 && cmdAgentCount >= 1) 
                    {
                        Emit(context,
                            "KeySpace",
                            Severity.Info,
                            $"Detected keyspace: `{groupByKeySpace.Key}` in stamp: `{groupByStamp.Key}` with `{cmdAgentCount}` CmdAgent machines but 0 GlobalCache machines",
                            groupByStamp.Key);
                    }
                }
            }
        }
    }
}
