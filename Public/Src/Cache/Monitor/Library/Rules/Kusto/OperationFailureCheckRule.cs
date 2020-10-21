// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Scheduling;
using BuildXL.Cache.Monitor.Library.Rules;
using Kusto.Data.Common;
using static BuildXL.Cache.Monitor.App.Analysis.Utilities;

namespace BuildXL.Cache.Monitor.App.Rules.Kusto
{
    internal class OperationFailureCheckRule : MultipleStampRuleBase
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

        public class Configuration : MultiStampRuleConfiguration
        {
            public Configuration(MultiStampRuleConfiguration kustoRuleConfiguration, Check check)
                : base(kustoRuleConfiguration)
            {
                Check = check;
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromMinutes(45);

            public Check Check { get; }
        }

        private readonly Configuration _configuration;

        /// <inheritdoc />
        public override string Identifier => $"{nameof(OperationFailureCheckRule)};{_configuration.Check.Name}:{_configuration.Environment}";

        public OperationFailureCheckRule(Configuration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

#pragma warning disable CS0649
        internal class Result
        {
            public string Component = string.Empty;
            public string Operation = string.Empty;
            public string ExceptionType = string.Empty;
            public long Count;
            public long Machines;
            public string Stamp = string.Empty;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            var now = _configuration.Clock.UtcNow;

            var query  = $@"
                    let end = now();
                    let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                    table('{_configuration.CacheTableName}')
                    | where PreciseTimeStamp between (start .. end)
                    | where Operation == '{_configuration.Check.Match}' and isnotempty(Duration)
                    | where Result == '{Constants.ResultCode.Faiilure}' // Looking only at failures
                    | project PreciseTimeStamp, Machine, Message, CorrelationId, Stamp, Operation, Component
                    | parse Message with * 'result=[' Result:string '].' *
                    | parse Result with 'Error=[' * '] Diagnostics=[' Diagnostics:string ']'
                    | parse Diagnostics with ExceptionType:string ': ' *
                    | extend Result=iif(isnull(Diagnostics) or isempty(Diagnostics), Result, '')
                    | extend Operation=iif(isnull(Operation) or isempty(Operation), 'Unknown', Operation)
                    | extend ExceptionType=iif(isnull(ExceptionType) or isempty(ExceptionType), 'Unknown', ExceptionType)
                    | project-away Message
                    | summarize Count=count(), Machines=dcount(Machine, 2) by Component, Operation, ExceptionType, Stamp
                    | where not(isnull(Machines))
                    | sort by Machines desc, Count desc";

            var results = await QueryKustoAsync<Result>(context, query);

            foreach (var result in results)
            {
                BiThreshold(result.Count, result.Machines, _configuration.Check.CountThresholds, _configuration.Check.MachinesThresholds, (severity, countThreshold, machinesThreshold) =>
                {
                    Emit(context, $"Errors_Operation_{_configuration.Check.Name}_{result.Component}.{result.Operation}", Severity.Info,
                        $"`{result.Machines}` machine(s) had `{result.Count}` errors (`{result.ExceptionType}`) in operation `{result.Component}.{result.Operation}`",
                        stamp: result.Stamp,
                        eventTimeUtc: now);
                });
            }
        }
    }
}
