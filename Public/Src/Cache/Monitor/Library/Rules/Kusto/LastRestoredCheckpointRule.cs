// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Scheduling;
using BuildXL.Cache.Monitor.Library.Rules;
using Kusto.Data.Common;
using static BuildXL.Cache.Monitor.App.Analysis.Utilities;

namespace BuildXL.Cache.Monitor.App.Rules.Kusto
{
    internal class LastRestoredCheckpointRule : MultipleStampRuleBase
    {
        public class Configuration : MultiStampRuleConfiguration
        {
            public Configuration(MultiStampRuleConfiguration kustoRuleConfiguration)
                : base(kustoRuleConfiguration)
            {
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromHours(2);

            public TimeSpan ActivityPeriod { get; set; } = TimeSpan.FromHours(1);

            public TimeSpan MasterActivityPeriod { get; set; } = TimeSpan.FromHours(1);

            public Thresholds<int> MissingRestoreMachinesThresholds = new Thresholds<int>()
            {
                Info = 5,
                Warning = 10,
                Error = 20,
                Fatal = 50,
            };

            public Thresholds<int> OldRestoreMachinesThresholds = new Thresholds<int>()
            {
                Info = 5,
                Warning = 10,
                Error = 20,
                Fatal = 50,
            };

            public TimeSpan CheckpointAgeErrorThreshold { get; set; } = TimeSpan.FromMinutes(45);
        }

        private readonly Configuration _configuration;

        /// <inheritdoc />
        public override string Identifier => $"{nameof(LastRestoredCheckpointRule)}:{_configuration.Environment}";

        public LastRestoredCheckpointRule(Configuration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

#pragma warning disable CS0649
        internal class Result
        {
            public string Machine = string.Empty;
            public DateTime LastActivityTime;
            public DateTime? LastRestoreTime;
            public TimeSpan? Age;
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
                let activity = end - {CslTimeSpanLiteral.AsCslString(_configuration.ActivityPeriod)};
                //
                // All the events limited by the given time range
                //
                let Events = CloudCacheLogEvent
                | where PreciseTimeStamp between (start .. end);
                //
                // Last events per stamp per worker machine
                // Machine, Stamp, LastActiveTime
                //
                let MachineLatestActivityEvents = Events
                | where PreciseTimeStamp >= activity
                | where Role == 'Worker'
                | summarize LastActivityTime=max(PreciseTimeStamp) by Machine, Stamp;
                //
                // The most recent restore checkpoint events
                //
                let RestoreCheckpointEvents = Events
                | where Component == 'CheckpointManager' and Operation == 'RestoreCheckpointAsync' and isnotempty(Duration)
                | where Result == '{Constants.ResultCode.Success}'
                | summarize LastRestoreTime=max(PreciseTimeStamp) by Machine, Stamp;
                //
                // Final results
                //
                MachineLatestActivityEvents 
                | join hint.strategy=broadcast kind=leftouter RestoreCheckpointEvents on Machine, Stamp
                | project-away Machine1, Stamp1
                | extend Age=LastActivityTime - LastRestoreTime";
            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

            GroupByStampAndCallHelper<Result>(results, result => result.Stamp, lastRestoredCheckpointHelper);

            void lastRestoredCheckpointHelper(string stamp, List<Result> results)
            {
                if (results.Count == 0)
                {
                    Emit(context, "NoLogs", Severity.Fatal,
                        $"No machines logged anything in the last `{_configuration.ActivityPeriod}`",
                        stamp,
                        eventTimeUtc: now);
                    return;
                }

                var missing = new List<Result>();
                var failures = new List<Result>();
                foreach (var result in results)
                {
                    if (!result.LastRestoreTime.HasValue)
                    {
                        missing.Add(result);
                        continue;
                    }

                    Contract.AssertNotNull(result.Age);
                    if (result.Age.Value >= _configuration.CheckpointAgeErrorThreshold)
                    {
                        failures.Add(result);
                    }
                }

                _configuration.MissingRestoreMachinesThresholds.Check(missing.Count, (severity, threshold) =>
                {
                    var formattedMissing = missing.Select(m => $"`{m.Machine}`");
                    var machinesCsv = string.Join(", ", formattedMissing);
                    var shortMachinesCsv = string.Join(", ", formattedMissing.Take(5));
                    Emit(context, "NoRestoresThreshold", severity,
                        $"Found `{missing.Count}` machine(s) active in the last `{_configuration.ActivityPeriod}`, but without checkpoints restored in at least `{_configuration.LookbackPeriod}`: {machinesCsv}",
                        stamp,
                        $"`{missing.Count}` machine(s) haven't restored checkpoints in at least `{_configuration.LookbackPeriod}`. Examples: {shortMachinesCsv}",
                        eventTimeUtc: now);
                });

                _configuration.OldRestoreMachinesThresholds.Check(failures.Count, (severity, threshold) =>
                {
                    var formattedFailures = failures.Select(f => $"`{f.Machine}` ({f.Age ?? TimeSpan.Zero})");
                    var machinesCsv = string.Join(", ", formattedFailures);
                    var shortMachinesCsv = string.Join(", ", formattedFailures.Take(5));
                    Emit(context, "OldRestores", severity,
                        $"Found `{failures.Count}` machine(s) active in the last `{_configuration.ActivityPeriod}`, but with old checkpoints (at least `{_configuration.CheckpointAgeErrorThreshold}`): {machinesCsv}",
                        stamp,
                        $"`{failures.Count}` machine(s) have checkpoints older than `{_configuration.CheckpointAgeErrorThreshold}`. Examples: {shortMachinesCsv}",
                        eventTimeUtc: now);
                });
            }
        }
    }
}
