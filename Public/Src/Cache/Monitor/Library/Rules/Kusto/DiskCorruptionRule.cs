// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.Monitor.App.Rules;
using BuildXL.Cache.Monitor.App.Scheduling;
using Kusto.Data.Common;
using static BuildXL.Cache.Monitor.App.Analysis.Utilities;

namespace BuildXL.Cache.Monitor.Library.Rules.Kusto
{
    internal class DiskCorruptionRule : MultipleStampRuleBase
    {
        public class Configuration : MultiStampRuleConfiguration
        {
            public Configuration(MultiStampRuleConfiguration other) : base(other)
            {
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromHours(1);

            public Thresholds<long> MachineCountThreshold = new Thresholds<long>()
            {
                Error = 0,
                Fatal = 3,
            };
        }

        public DiskCorruptionRule(Configuration configuration) : base(configuration) => _configuration = configuration;

        /// <inheritdoc />
        public override string Identifier => $"{nameof(DiskCorruptionRule)}:{_configuration.Environment}";

        private readonly Configuration _configuration;

#pragma warning disable CS0649
        internal record Result
        {
            public long Count;
            public string Component = string.Empty;
            public string Operation = string.Empty;
            public string Machine = string.Empty;
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
                table('{_configuration.CacheTableName}')
                | where PreciseTimeStamp between(start .. end)
                | where Component != 'DistributedContentCopier'
                | where Message has_any('The file or directory is corrupted and unreadable', 'Corruption: block checksum mismatch: ', 'IOException: Incorrect function')  
                | project PreciseTimeStamp, Stamp, Machine, Component, Operation, Message
                | summarize Count = count(), any(Message) by Component, Operation, Machine, Stamp
                | summarize arg_max(Count, Component, Operation, any_Message) by Machine, Stamp";

            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

            GroupByStampAndCallHelper<Result>(results, result => result.Stamp, corruptionRuleHelper);

            void corruptionRuleHelper(string stamp, List<Result> results)
            {
                _configuration.MachineCountThreshold.Check(results.Count, (severity, pct) =>
                { 
                    var machinesNames = string.Join(", ", results.Select(result => $"`{result.Machine}`"));
                    Emit(context, "MachineCorruption", severity,
                        $"{results.Count} machines have had corruption issues in the last {_configuration.LookbackPeriod}: {machinesNames}",
                        stamp,
                        eventTimeUtc: now);
                });
            }
        }
    }
}
