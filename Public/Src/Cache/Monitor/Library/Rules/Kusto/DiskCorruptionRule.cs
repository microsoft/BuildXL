// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Rules;
using BuildXL.Cache.Monitor.App.Scheduling;
using Kusto.Data.Common;

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
        }

        public DiskCorruptionRule(Configuration configuration) : base(configuration) => _configuration = configuration;

        /// <inheritdoc />
        public override string Identifier => $"{nameof(DiskCorruptionRule)}:{_configuration.Environment}";

        private readonly Configuration _configuration;

#pragma warning disable CS0649
        internal record Result
        {
            public string Machine = string.Empty;
            public string Stamp = string.Empty;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            var now = _configuration.Clock.UtcNow;
            var query =
                $@"
                let end = now(); let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                let RocksDbErrors = table('{_configuration.CacheTableName}')
                | where PreciseTimeStamp between(start .. end)
                | where Component != 'DistributedContentCopier'
                | where Message has_any('The file or directory is corrupted and unreadable', 'Corruption: block checksum mismatch: ', 'IOException: Incorrect function')  
                | project PreciseTimeStamp, Stamp, Machine, Component, Operation, Message
                | summarize Count = count(), any(Message) by Component, Operation, Machine, Stamp
                | distinct Stamp, Machine;
                let SelfCheckErrors = table('{_configuration.CacheTableName}')
                | where PreciseTimeStamp between (start .. end)
                | where Operation == 'SelfCheckContentDirectoryAsync'
                | where isnotempty(Duration)
                | parse Message with * 'InvalidFiles=' InvalidFiles: long ', TotalProcessedFiles=' TotalProcessedFiles: long
                | where InvalidFiles > 0
                | project Stamp, Machine, Ratio = (toreal(InvalidFiles) / TotalProcessedFiles) * 100
                | where Ratio > 1
                | project-away Ratio;
                RocksDbErrors
                | union SelfCheckErrors
                | distinct Stamp, Machine
                | sort by Stamp asc";

            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

            if (results.Count > 0)
            {
                var envStr = _configuration.Environment == App.MonitorEnvironment.CloudBuildProduction ? "PROD" : "TEST";

                var commands = string.Join("\n", results.Select(r => $"Reimage-Machine {envStr} {r.Stamp} {r.Machine}"));
                var stamps = string.Join(", ", results.Select(r => r.Stamp).Distinct());

                Emit(context, "MachineCorruption", Severity.Warning,
                    $"{results.Count} machines have had corruption issues in the last {_configuration.LookbackPeriod}:\n{commands}",
                    stamps,
                    eventTimeUtc: now);
            }
        }
    }
}
