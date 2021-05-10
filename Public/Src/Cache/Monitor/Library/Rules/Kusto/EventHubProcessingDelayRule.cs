// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Scheduling;
using BuildXL.Cache.Monitor.Library.Analysis;
using BuildXL.Cache.Monitor.Library.Rules;
using Kusto.Data.Common;
using static BuildXL.Cache.Monitor.App.Analysis.Utilities;

namespace BuildXL.Cache.Monitor.App.Rules.Kusto
{
    internal class EventHubProcessingDelayRule : MultipleStampRuleBase
    {
        public class Configuration : MultiStampRuleConfiguration
        {
            public Configuration(MultiStampRuleConfiguration kustoRuleConfiguration)
                : base(kustoRuleConfiguration)
            {
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromHours(2);

            public TimeSpan BinWidth { get; set; } = TimeSpan.FromMinutes(10);

            public Thresholds<TimeSpan> Thresholds = new Thresholds<TimeSpan>()
            {
                Warning = TimeSpan.FromMinutes(30),
                Error = TimeSpan.FromHours(1),
            };

            public IcmThresholds<double> EventProcessingDelayIcmThresholdsInHours = new IcmThresholds<double>()
            {
                Sev2 = 2,
            };
        }

        private readonly Configuration _configuration;

        /// <inheritdoc />
        public override string Identifier => $"{nameof(EventHubProcessingDelayRule)}:{_configuration.Environment}";

        public EventHubProcessingDelayRule(Configuration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

#pragma warning disable CS0649
        internal class Result
        {
            public string Machine = string.Empty;
            public DateTime PreciseTimeStamp;
            public TimeSpan MaxDelay;
            public string Stamp = string.Empty;
        }

        internal class Result2
        {
            public long? OutstandingBatches;
            public long? OutstandingEvents;
            public long? ProcessedBatches;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            var now = _configuration.Clock.UtcNow;
            var query =
                $@"
                let end = now();
                let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                let binWidth = {CslTimeSpanLiteral.AsCslString(_configuration.BinWidth)};
                let MasterEvents = table('{_configuration.CacheTableName}')
                | where PreciseTimeStamp between (start .. end)
                | where Role == 'Master'
                | where Component has 'EventHubContentLocationEventStore' or Component has 'ContentLocationEventStore';
                let MaximumDelayFromReceivedEvents = MasterEvents
                | where Message has 'ReceivedEvent'
                | parse Message with * 'ProcessingDelay=' Lag:timespan ',' *
                | project PreciseTimeStamp, Stamp, Machine, ServiceVersion, Message, Duration=Lag
                | summarize MaxDelay=arg_max(Duration, Machine) by Stamp, PreciseTimeStamp=bin(PreciseTimeStamp, binWidth);
                MaximumDelayFromReceivedEvents
                | project Stamp, Machine, PreciseTimeStamp, MaxDelay
                | order by PreciseTimeStamp desc";
            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

            await GroupByStampAndCallHelperAsync<Result>(results, result => result.Stamp, eventHubProcessingDelayHelper);

            async Task eventHubProcessingDelayHelper(string stamp, List<Result> results)
            {
                if (results.Count == 0)
                {
                    // If we reached this point, it means we don't have messages from master saying that it processed
                    // anything. We now need to check if clients actually sent anything at all!
                    var outstandingQuery = $@"
                    let end = now();
                    let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                    let Events = table('{_configuration.CacheTableName}')
                    | where PreciseTimeStamp between (start .. end)
                    | where Stamp == '{stamp}'
                    | where Component has 'EventHubContentLocationEventStore' or Component has 'ContentLocationEventStore'
                    | project PreciseTimeStamp, CorrelationId, Role, Machine, Stamp, Message;
                    let Enqueued = Events
                    | where Role == 'Worker'
                    | where Message has 'sending' and Message has 'OpId'
                    | parse Message with * '/' NumEvents:long 'event.' *
                    | project-away Message;
                    let Processed = Events
                    | where Role == 'Master'
                    | where Message has 'OpId'
                    | project-away Message;
                    let Outstanding = Enqueued
                    | join kind=leftanti Processed on CorrelationId
                    | summarize OutstandingBatches=dcount(CorrelationId), OutstandingEvents=sum(NumEvents);
                    let Done = Processed
                    | summarize ProcessedBatches=dcount(CorrelationId);             
                    Outstanding | extend dummy=1 | join kind=inner (Done | extend dummy=1) on dummy | project-away dummy, dummy1";

                    var outstandingResults = (await QueryKustoAsync<Result2>(context, outstandingQuery)).ToList();

                    if (outstandingResults.Count != 1)
                    {
                        Emit(context, "NoLogs", Severity.Fatal,
                            $"No events processed for at least `{_configuration.LookbackPeriod}`",
                            stamp,
                            eventTimeUtc: now);

                        await EmitIcmAsync(
                            severity: _configuration.Environment.IsProduction() ? 2 : 3,
                            title: $"{stamp}: master event processing issues",
                            stamp,
                            machines: null,
                            correlationIds: null,
                            description: $"No events processed for at least `{_configuration.LookbackPeriod}`",
                            eventTimeUtc: now,
                            cacheTimeToLive: _configuration.LookbackPeriod);

                        return;
                    }

                    var result = outstandingResults[0];
                    if (result.OutstandingBatches.HasValue && result.OutstandingBatches.Value > 0)
                    {
                        if (result.ProcessedBatches.HasValue && result.ProcessedBatches.Value == 0)
                        {
                            Emit(context, "MasterStuck", Severity.Fatal,
                                $"Master hasn't processed any events in the last `{_configuration.LookbackPeriod}`, but has `{result.OutstandingBatches.Value}` batches pending.",
                                stamp,
                                eventTimeUtc: now);

                            await EmitIcmAsync(
                                severity: _configuration.Environment.IsProduction() ? 2 : 3,
                                title: $"{stamp}: master event processing issues",
                                stamp,
                                machines: null,
                                correlationIds: null,
                                description: $"Master hasn't processed any events in the last `{_configuration.LookbackPeriod}`, but has `{result.OutstandingBatches.Value}` batches pending.",
                                eventTimeUtc: now,
                                cacheTimeToLive: _configuration.LookbackPeriod);
                        }
                    }

                    return;
                }

                var delay = results[0].MaxDelay;
                _configuration.Thresholds.Check(delay, (severity, threshold) =>
                {
                    Emit(context, "DelayThreshold", severity,
                        $"EventHub processing delay `{delay}` above threshold `{threshold}`. Master is `{results[0].Machine}`",
                        stamp,
                        eventTimeUtc: results[0].PreciseTimeStamp);
                });

                await _configuration.EventProcessingDelayIcmThresholdsInHours.CheckAsync(delay.TotalHours, (severity, threshold) =>
                {
                    if (severity >= 2)
                    {
                        if (!_configuration.Environment.IsProduction())
                        {
                            severity = 3;
                        }
                        else if (!TimeConstraints.BusinessHours.SatisfiedPST(utcNow: _configuration.Clock.UtcNow))
                        {
                            severity = 3;
                        }
                    }

                    return EmitIcmAsync(
                        severity,
                        title: $"{stamp}: master event processing issues",
                        stamp,
                        machines: new[] { results[0].Machine },
                        correlationIds: null,
                        description: $"EventHub processing delay `{delay}` above threshold `{threshold}`. Master is `{results[0].Machine}`",
                        eventTimeUtc: now,
                        cacheTimeToLive: _configuration.LookbackPeriod);
                });
            }
        }
    }
}
