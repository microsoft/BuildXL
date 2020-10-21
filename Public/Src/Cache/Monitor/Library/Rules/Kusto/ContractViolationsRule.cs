// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Scheduling;
using BuildXL.Cache.Monitor.Library.Rules;
using Kusto.Data.Common;

namespace BuildXL.Cache.Monitor.App.Rules.Kusto
{
    internal class ContractViolationsRule : MultipleStampRuleBase
    {
        public class Configuration : MultiStampRuleConfiguration
        {
            public Configuration(MultiStampRuleConfiguration kustoRuleConfiguration)
                : base(kustoRuleConfiguration)
            {
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromMinutes(30);
        }

        private readonly Configuration _configuration;

        /// <inheritdoc />
        public override string Identifier => $"{nameof(ContractViolationsRule)}:{_configuration.Environment}";

        public ContractViolationsRule(Configuration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

#pragma warning disable CS0649
        internal class Result
        {
            public string ExceptionType = string.Empty;
            public string ExceptionMessage = string.Empty;
            public string Operation = string.Empty;
            public long Machines;
            public long Count;
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
                | where PreciseTimeStamp between (start .. end)
                | where Result == '{Constants.ResultCode.CriticalFailure}'
                | project PreciseTimeStamp, Machine, Message, Stamp, Component, Operation
                | parse Message with * 'occurred: ' ErrorMessage:string 'Diagnostics=[' Exception:string
                | extend Operation = strcat(Component, '.', Operation)
                | parse Exception with ExceptionType:string ': ' ExceptionMessage '\n' * // Fetch first line of the exception
                | project-away Message, ErrorMessage
                | where ExceptionType != 'System.UnauthorizedAccessException'
                | summarize Machines=dcount(Machine, 2), Count=count() by ExceptionType, ExceptionMessage, Operation, Stamp
                | where not(isnull(Machines))";
            var results = await QueryKustoAsync<Result>(context, query);

            foreach (var result in results)
            {
                Emit(context, $"ContractViolations_Operation_{result.Operation}", Severity.Error,
                    $"`{result.Machines}` machine(s) had `{result.Count}` contract violations (`{result.ExceptionType}`) in operation `{result.Operation}`. Example message: {result.ExceptionMessage}",
                    stamp: result.Stamp,
                    $"`{result.Machines}` machine(s) had `{result.Count}` contract violations (`{result.ExceptionType}`) in operation `{result.Operation}`",
                    eventTimeUtc: now);
            }
        }
    }
}
