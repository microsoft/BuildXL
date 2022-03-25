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

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromMinutes(30);

            public TimeSpan ActivityPeriod { get; set; } = TimeSpan.FromMinutes(10);
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
                let end = now(); let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)}; let activity = end - {CslTimeSpanLiteral.AsCslString(_configuration.ActivityPeriod)};
                let NtfsCorruptionEvents = SystemEvents
                | where PreciseTimeStamp between(start .. end)
                | where EventID == 55
                | where ProviderName == 'Ntfs'
                | summarize NtfsCorruptionEvents=count() by Machine=RoleInstance;
                let SelfCheckErrors = CloudCacheLogEvent
                | where PreciseTimeStamp between(start .. end)
                | where Operation in ('SelfCheckContentDirectoryAsync', 'SelfCheckContentDirectoryCoreAsync')
                | where Message has 'InvalidFiles' and Message !has 'InvalidFiles=0,'
                | parse Message with * 'InvalidFiles=' InvalidFiles:long ', ' *
                | project PreciseTimeStamp, Environment, Stamp, Machine, InvalidFiles
                | summarize SelfCheckErrors=sum(InvalidFiles) by Machine;
                let CacheCorruptionErrors = CloudCacheLogEvent
                | where PreciseTimeStamp between(start .. end)
                | where Component != 'DistributedContentCopier' and Operation !contains 'Proactive'
                | where Message has_any('The file or directory is corrupted and unreadable', 'Corruption: block checksum mismatch: ', 'IOException: Incorrect function')  
                | project PreciseTimeStamp, Stamp, Machine, Component, Operation, Message
                | summarize CacheCorruptionErrors=count() by Machine;
                CloudCacheLogEvent
                | where PreciseTimeStamp between(activity .. end)
                | distinct Stamp, Machine
                | join kind=leftouter NtfsCorruptionEvents on Machine
                | join kind=leftouter SelfCheckErrors on Machine
                | join kind=leftouter CacheCorruptionErrors on Machine
                | project-away Machine1, Machine2, Machine3
                | extend NtfsCorruptionEvents=coalesce(NtfsCorruptionEvents, 0), SelfCheckErrors=coalesce(SelfCheckErrors, 0), CacheCorruptionErrors=coalesce(CacheCorruptionErrors, 0)
                | where NtfsCorruptionEvents + SelfCheckErrors + CacheCorruptionErrors> 0
                | where NtfsCorruptionEvents > 0 or CacheCorruptionErrors > 0 or SelfCheckErrors > 100
                | distinct Stamp, Machine
                | sort by Stamp, Machine";

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
