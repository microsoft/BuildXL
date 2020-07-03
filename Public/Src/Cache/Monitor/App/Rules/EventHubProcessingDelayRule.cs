using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using Kusto.Data.Common;
using static BuildXL.Cache.Monitor.App.Utilities;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal class EventHubProcessingDelayRule : KustoRuleBase
    {
        public class Configuration : KustoRuleConfiguration
        {
            public Configuration(KustoRuleConfiguration kustoRuleConfiguration)
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
        }

        private readonly Configuration _configuration;

        public override string Identifier => $"{nameof(EventHubProcessingDelayRule)}:{_configuration.Environment}/{_configuration.Stamp}";

        public EventHubProcessingDelayRule(Configuration configuration)
            : base(configuration)
        {
            Contract.RequiresNotNull(configuration);
            _configuration = configuration;
        }

#pragma warning disable CS0649
        private class Result
        {
            public string Machine;
            public DateTime PreciseTimeStamp;
            public TimeSpan MaxDelay;
            public TimeSpan AvgProcessingDuration;
            public TimeSpan MaxProcessingDuration;
            public long MessageCount;
            public long SumMessageCount;
            public long SumMessageSizeMb;
        }

        private class Result2
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
                let MasterEvents = table(""{_configuration.CacheTableName}"")
                | where PreciseTimeStamp between (start .. end)
                | where Stamp == ""{_configuration.Stamp}""
                | where Service == ""{Constants.MasterServiceName}"";
                let MaximumDelayFromReceivedEvents = MasterEvents
                | where Message has ""ReceivedEvent""
                | parse Message with * ""ProcessingDelay="" Lag:timespan "","" *
                | project PreciseTimeStamp, Stamp, Machine, ServiceVersion, Message, Duration=Lag
                | summarize MaxDelay=max(Duration) by Stamp, PreciseTimeStamp=bin(PreciseTimeStamp, {CslTimeSpanLiteral.AsCslString(_configuration.BinWidth)});
                let MaximumDelayFromEH = MasterEvents
                | where (Message has ""EventHubContentLocationEventStore"" and Message has ""processed"") 
                | parse Message with * ""processed "" MessageCount:long ""message(s) by "" Duration:long ""ms"" * ""TotalMessagesSize="" TotalSize:long *
                | project PreciseTimeStamp, Stamp, MessageCount, Duration, TotalSize, Message, Machine
                | summarize MessageCount=count(), ProcessingDurationMs=percentile(Duration, 50), MaxProcessingDurationMs=max(Duration), SumMessageCount=sum(MessageCount), SumMessageSize=sum(TotalSize) by Stamp, Machine, PreciseTimeStamp=bin(PreciseTimeStamp, {CslTimeSpanLiteral.AsCslString(_configuration.BinWidth)});
                MaximumDelayFromReceivedEvents
                | join MaximumDelayFromEH on Stamp, PreciseTimeStamp
                | project Machine, PreciseTimeStamp, MaxDelay, AvgProcessingDuration=totimespan(ProcessingDurationMs*10000), MaxProcessingDuration=totimespan(MaxProcessingDurationMs*10000), MessageCount, SumMessageCount, SumMessageSizeMb=SumMessageSize / (1024*1024)
                | order by PreciseTimeStamp desc";
            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

            if (results.Count == 0)
            {
                // If we reached this point, it means we don't have messages from master saying that it processed
                // anything. We now need to check if clients actually sent anything at all!
                var outstandingQuery = $@"
                    let end = now();
                    let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                    let Events = table(""{_configuration.CacheTableName}"")
                    | where PreciseTimeStamp between (start .. end)
                    | where Stamp == ""{_configuration.Stamp}""
                    | project PreciseTimeStamp, Service, Machine, Stamp, LogLevelFriendly, Message;
                    let Enqueued = Events
                    | where Service == ""{Constants.ServiceName}""
                    | where Message has ""EventHubContentLocationEventStore"" and Message has ""sending"" and Message has ""OpId""
                    | parse Message with * ""/"" NumEvents:long ""event."" * ""OpId="" OperationId "","" *
                    | project-away Message;
                    let Processed = Events
                    | where Service == ""{Constants.MasterServiceName}""
                    | where Message has ""EventHubContentLocationEventStore"" and Message has ""OpId""
                    | parse Message with * ""OpId="" OperationId "","" *
                    | project-away Message;
                    let Outstanding = Enqueued
                    | join kind=leftanti Processed on OperationId
                    | summarize OutstandingBatches=dcount(OperationId), OutstandingEvents=sum(NumEvents);
                    let Done = Processed
                    | summarize ProcessedBatches=dcount(OperationId);
                    Outstanding | extend dummy=1 | join kind=inner (Done | extend dummy=1) on dummy | project-away dummy, dummy1";

                var outstandingResults = (await QueryKustoAsync<Result2>(context, outstandingQuery)).ToList();

                if (outstandingResults.Count != 1)
                {
                    Emit(context, "NoLogs", Severity.Fatal,
                        $"No events processed for at least `{_configuration.LookbackPeriod}`",
                        eventTimeUtc: now);
                    return;
                }

                var result = outstandingResults[0];
                if (result.OutstandingBatches.HasValue && result.OutstandingBatches.Value > 0)
                {
                    if (result.ProcessedBatches.HasValue && result.ProcessedBatches.Value == 0)
                    {
                        Emit(context, "MasterStuck", Severity.Fatal,
                            $"Master hasn't processed any events in the last `{_configuration.LookbackPeriod}`, but has `{result.OutstandingBatches.Value}` batches pending.",
                            eventTimeUtc: now);
                    }
                }

                return;
            }

            var delay = results[0].MaxDelay;
            _configuration.Thresholds.Check(delay, (severity, threshold) =>
            {
                Emit(context, "DelayThreshold", severity,
                    $"EventHub processing delay `{delay}` above threshold `{threshold}`. Master is `{results[0].Machine}`",
                    eventTimeUtc: results[0].PreciseTimeStamp);
            });
        }
    }
}
