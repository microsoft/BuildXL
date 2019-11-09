using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Utilities;
using Kusto.Data.Common;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal class LastRestoredCheckpointRule : KustoRuleBase
    {
        public class Configuration : KustoRuleConfiguration
        {
            public Configuration(KustoRuleConfiguration kustoRuleConfiguration)
                : base(kustoRuleConfiguration)
            {
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromHours(2);

            public TimeSpan ActivityThreshold { get; set; } = TimeSpan.FromHours(1);

            public int MissingRestoreMachinesThreshold { get; set; } = 20;

            public int OldRestoreMachinesThreshold { get; set; } = 5;

            public TimeSpan CheckpointAgeErrorThreshold { get; set; } = TimeSpan.FromMinutes(45);
        }

        private readonly Configuration _configuration;

        public override string Identifier => $"{nameof(LastRestoredCheckpointRule)}:{_configuration.Environment}/{_configuration.Stamp}";

        public LastRestoredCheckpointRule(Configuration configuration)
            : base(configuration)
        {
            Contract.RequiresNotNull(configuration);
            _configuration = configuration;
        }

#pragma warning disable CS0649
        private class Result
        {
            public string Machine;
            public DateTime LastActivityTime;
            public DateTime? LastRestoreTime;
            public TimeSpan? Age;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            var now = _configuration.Clock.UtcNow - Constants.KustoIngestionDelay;
            var query =
                $@"
                let end = now() - {CslTimeSpanLiteral.AsCslString(Constants.KustoIngestionDelay)};
                let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                let activity = end - {CslTimeSpanLiteral.AsCslString(_configuration.ActivityThreshold)};
                let Events = CloudBuildLogEvent
                | where PreciseTimeStamp between (start .. end)
                | where Stamp == ""{_configuration.Stamp}""
                | where Service == ""{Constants.ServiceName}"" or Service == ""{Constants.MasterServiceName}"";
                let Machines = Events
                | where PreciseTimeStamp >= activity
                | summarize LastActivityTime=max(PreciseTimeStamp) by Machine;
                let Restores = Events
                | where Message has ""RestoreCheckpointAsync stop""
                | summarize LastRestoreTime=max(PreciseTimeStamp) by Machine;
                Machines
                | join hint.strategy=broadcast kind=leftouter Restores on Machine
                | project-away Machine1
                | extend Age=LastActivityTime - LastRestoreTime
                | where not(isnull(Machine))";
            var results = (await QuerySingleResultSetAsync<Result>(query)).ToList();

            if (results.Count == 0)
            {
                Emit(context, "NoLogs", Severity.Fatal,
                    $"No machines logged anything in the last day",
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

                if (result.Age.Value >= _configuration.CheckpointAgeErrorThreshold)
                {
                    failures.Add(result);
                }
            }

            Utilities.SeverityFromThreshold(missing.Count, 1, _configuration.MissingRestoreMachinesThreshold, (severity, threshold) =>
            {
                var formattedMissing = missing.Select(m => $"`{m.Machine}`");
                var machinesCsv = string.Join(", ", formattedMissing);
                var shortMachinesCsv = string.Join(", ", formattedMissing.Take(5));
                Emit(context, "NoRestoresThreshold", severity,
                    $"Found {missing.Count} machine(s) active in the last `{_configuration.ActivityThreshold}`, but without checkpoints restored in at least `{_configuration.LookbackPeriod}`: {machinesCsv}",
                    $"`{missing.Count}` machine(s) haven't restored checkpoints in at least `{_configuration.LookbackPeriod}`. Examples: {shortMachinesCsv}",
                    eventTimeUtc: now);
            });

            Utilities.SeverityFromThreshold(failures.Count, 1, _configuration.OldRestoreMachinesThreshold, (severity, threshold) =>
            {
                var formattedFailures = failures.Select(f => $"`{f.Machine}` ({f.Age.Value})");
                var machinesCsv = string.Join(", ", formattedFailures);
                var shortMachinesCsv = string.Join(", ", formattedFailures.Take(5));
                Emit(context, "OldRestores", severity,
                    $"Found `{failures.Count}` machine(s) active in the last `{_configuration.ActivityThreshold}`, but with old checkpoints (at least `{_configuration.CheckpointAgeErrorThreshold}`): {machinesCsv}",
                    $"`{failures.Count}` machine(s) have checkpoints older than `{_configuration.CheckpointAgeErrorThreshold}`. Examples: {shortMachinesCsv}",
                    eventTimeUtc: now);
            });
        }
    }
}
