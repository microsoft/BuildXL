// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Scheduling;
using Kusto.Data.Common;
using static BuildXL.Cache.Monitor.App.Analysis.Utilities;

namespace BuildXL.Cache.Monitor.App.Rules.Kusto
{
    internal class OperationFailureCheckRule : KustoRuleBase
    {
        public class Check
        {
            public string Match { get; set; } = string.Empty;

            private string? _name = null;

            public string Name
            {
                get => _name ?? Match;
                set => _name = value;
            }

            public Thresholds<long> CountThresholds { get; set; } = new Thresholds<long>()
            {
                Info = 1,
                Warning = 10,
                Error = 50,
            };

            public Thresholds<long> MachinesThresholds { get; set; } = new Thresholds<long>()
            {
                Info = 1,
                Error = 5,
                Fatal = 50,
            };
        }

        public class Configuration : KustoRuleConfiguration
        {
            public Configuration(KustoRuleConfiguration kustoRuleConfiguration, Check check)
                : base(kustoRuleConfiguration)
            {
                Check = check;
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromMinutes(45);

            public Check Check { get; }
        }

        private readonly Configuration _configuration;

        public override string Identifier => $"{nameof(OperationFailureCheckRule)};{_configuration.Check.Name}:{_configuration.StampId}";

        public OperationFailureCheckRule(Configuration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

#pragma warning disable CS0649
        private class Result
        {
            public string Operation = string.Empty;
            public string ExceptionType = string.Empty;
            public long Count;
            public long Machines;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            var now = _configuration.Clock.UtcNow;

            var query = string.Empty;
            if (_configuration.CacheTableName == Constants.OldTableName)
            {
                query = $@"
                    let end = now();
                    let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                    table(""{_configuration.CacheTableName}"")
                    | where PreciseTimeStamp between (start .. end)
                    | where Stamp == ""{_configuration.Stamp}""
                    | where Service == ""{Constants.ServiceName}"" or Service == ""{Constants.MasterServiceName}""
                    | where Message has ""{_configuration.Check.Match} stop ""
                    | where Message !has ""result=[Success"" // Looking only at failures
                    | where Message !has ""Critical error occurred"" // Critical errors are handled by a different rule
                    | project PreciseTimeStamp, Machine, Message
                    | parse Message with CorrelationId:string "" "" Operation:string "" stop "" * ""result=["" Result:string ""]."" *
                    | where Result != ""OperationCancelled""
                    | extend Operation=extract(""^((\\w+).(\\w+)).*"", 1, Operation) // Prune unnecessary parameters
                    | parse Result with ""Error=["" * ""] Diagnostics=["" Diagnostics:string ""]""
                    | parse Diagnostics with ExceptionType:string "": "" *
                    | extend Result=iif(isnull(Diagnostics) or isempty(Diagnostics), Result, """")
                    | extend Operation=iif(isnull(Operation) or isempty(Operation), ""Unknown"", Operation)
                    | extend ExceptionType=iif(isnull(ExceptionType) or isempty(ExceptionType), ""Unknown"", ExceptionType)
                    | project-away Message
                    | summarize Count=count(), Machines=dcount(Machine, 2) by Operation, ExceptionType
                    | where not(isnull(Machines))
                    | sort by Machines desc, Count desc";
            }
            else
            {
                query = $@"
                    let end = now();
                    let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                    table(""{_configuration.CacheTableName}"")
                    | where PreciseTimeStamp between (start .. end)
                    | where Stamp == ""{_configuration.Stamp}""
                    | where Service == ""{Constants.ServiceName}"" or Service == ""{Constants.MasterServiceName}""
                    | where Message has ""{_configuration.Check.Match} stop ""
                    | where Message !has ""result=[Success"" // Looking only at failures
                    | where Message !has ""Critical error occurred"" // Critical errors are handled by a different rule
                    | project PreciseTimeStamp, Machine, Message, CorrelationId
                    | parse Message with Operation:string "" stop "" * ""result=["" Result:string ""]."" *
                    | where Result != ""OperationCancelled""
                    | extend Operation=extract(""^((\\w+).(\\w+)).*"", 1, Operation) // Prune unnecessary parameters
                    | parse Result with ""Error=["" * ""] Diagnostics=["" Diagnostics:string ""]""
                    | parse Diagnostics with ExceptionType:string "": "" *
                    | extend Result=iif(isnull(Diagnostics) or isempty(Diagnostics), Result, """")
                    | extend Operation=iif(isnull(Operation) or isempty(Operation), ""Unknown"", Operation)
                    | extend ExceptionType=iif(isnull(ExceptionType) or isempty(ExceptionType), ""Unknown"", ExceptionType)
                    | project-away Message
                    | summarize Count=count(), Machines=dcount(Machine, 2) by Operation, ExceptionType
                    | where not(isnull(Machines))
                    | sort by Machines desc, Count desc";
            }

            var results = await QueryKustoAsync<Result>(context, query);

            foreach (var result in results)
            {
                BiThreshold(result.Count, result.Machines, _configuration.Check.CountThresholds, _configuration.Check.MachinesThresholds, (severity, countThreshold, machinesThreshold) =>
                {
                    Emit(context, $"Errors_Operation_{_configuration.Check.Name}_{result.Operation}", Severity.Info,
                        $"`{result.Machines}` machine(s) had `{result.Count}` errors (`{result.ExceptionType}`) in operation `{result.Operation}`",
                        eventTimeUtc: now);
                });
            }
        }
    }
}
