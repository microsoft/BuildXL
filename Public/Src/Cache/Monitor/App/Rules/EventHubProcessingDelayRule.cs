using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using Kusto.Data.Common;

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

            public TimeSpan WarningThreshold { get; set; } = TimeSpan.FromMinutes(30);

            public TimeSpan ErrorThreshold { get; set; } = TimeSpan.FromMinutes(60);
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
#pragma warning restore CS0649

        public override async Task Run()
        {
            var ruleRunTimeUtc = _configuration.Clock.UtcNow;

            var query =
                $@"let MasterEvents = CloudBuildLogEvent
                | where Stamp == ""{_configuration.Stamp}""
                | where PreciseTimeStamp > ago({CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)})
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
            var results = (await QuerySingleResultSetAsync<Result>(query)).ToList();

            var now = _configuration.Clock.UtcNow;
            if (results.Count == 0)
            {
                Emit(Severity.Fatal,
                    $"No events processed for at least {_configuration.LookbackPeriod}",
                    ruleRunTimeUtc: ruleRunTimeUtc);
                return;
            }

            var delay = results[0].MaxDelay;

            if (delay >= _configuration.WarningThreshold)
            {
                var severity = Severity.Warning;
                var threshold = _configuration.WarningThreshold;
                if (delay >= _configuration.ErrorThreshold)
                {
                    severity = Severity.Error;
                    threshold = _configuration.ErrorThreshold;
                }

                Emit(severity,
                    $"EventHub processing delay `{delay}` above threshold `{threshold}`. Master is {results[0].Machine}",
                    ruleRunTimeUtc: ruleRunTimeUtc,
                    eventTimeUtc: results[0].PreciseTimeStamp);
            }
        }
    }
}
