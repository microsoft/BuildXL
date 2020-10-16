using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.Monitor.App.Notifications;
using BuildXL.Cache.Monitor.App.Rules;
using BuildXL.Cache.Monitor.App.Scheduling;
using Kusto.Cloud.Platform.Utils;

namespace BuildXL.Cache.Monitor.Library.Rules
{
    internal abstract class MultipleStampRuleBase : KustoRuleBase
    {
        private readonly MultiStampRuleConfiguration _configuration;

        /// <summary>
        /// Identifier corresponding to an environment as opposed to specific stamp
        /// </summary>
        public abstract override string Identifier { get; }

        public MultipleStampRuleBase(MultiStampRuleConfiguration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

        protected void Emit(
            RuleContext context,
            string bucket,
            Severity severity,
            string message,
            string stamp,
            string? summary = null,
            DateTime? eventTimeUtc = null)
        {
            Contract.RequiresNotNullOrEmpty(stamp);

            var now = _configuration.Clock.UtcNow;
            _configuration.Logger.Log(severity, $"[{Identifier}] {message}");
            _configuration.Notifier.Emit(new Notification(
                $"{Identifier}/{stamp}",
                context.RunGuid,
                context.RunTimeUtc,
                now,
                eventTimeUtc ?? now,
                bucket,
                severity,
                _configuration.Environment,
                stamp,
                message,
                summary ?? message));
        }

        protected void GroupByStampAndCallHelper<T>(List<T> results, Func<T, string> stampSelector, Action<string, List<T>> helperFunc)
        {
            var grouping = results.GroupBy(stampSelector);
            var missingStamps = _configuration.WatchList.EnvStamps[_configuration.Environment].Where(stamp => !grouping.Any(group => group.Key == stamp.Name));
            foreach (var group in grouping)
            {
                helperFunc(group.Key, group.ToList());
            }

            foreach (var stamp in missingStamps)
            {
                helperFunc(stamp.Name, new List<T>());
            }
        }
    }
}
