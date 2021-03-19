using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Notifications;
using BuildXL.Cache.Monitor.App.Rules;
using BuildXL.Cache.Monitor.App.Scheduling;
using BuildXL.Cache.Monitor.Library.IcM;

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
            _configuration.Logger.Log(severity, $"[{Identifier}/{stamp}] {message}");
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

        protected Task EmitIcmAsync(
            int severity,
            string title,
            string stamp,
            IEnumerable<string>? machines,
            IEnumerable<string>? correlationIds,
            string? description = null,
            DateTime? eventTimeUtc = null,
            TimeSpan? cacheTimeToLive = null)
        {
            var incident = new IcmIncident(stamp, _configuration.Environment.ToString(), machines, correlationIds, severity, description ?? title, title, eventTimeUtc, cacheTimeToLive);
            return _configuration.IcmClient.EmitIncidentAsync(incident);
        }

        protected void GroupByStampAndCallHelper<T>(List<T> results, Func<T, string> stampSelector, Action<string, List<T>> helperFunc)
        {
            var grouping = results.GroupBy(stampSelector);
            var missingStamps = _configuration.WatchList.EnvStamps[_configuration.Environment]
                .Where(stamp => !grouping.Any(group => group.Key == stamp.Name));
            foreach (var group in grouping)
            {
                if (string.IsNullOrWhiteSpace(group.Key))
                {
                    continue;
                }

                helperFunc(group.Key, group.ToList());
            }

            foreach (var stamp in missingStamps)
            {
                helperFunc(stamp.Name, new List<T>());
            }
        }

        protected async Task GroupByStampAndCallHelperAsync<T>(List<T> results, Func<T, string> stampSelector, Func<string, List<T>, Task> helperFunc)
        {
            var grouping = results.GroupBy(stampSelector);
            var missingStamps = _configuration.WatchList.EnvStamps[_configuration.Environment]
                .Where(stamp => !grouping.Any(group => group.Key == stamp.Name));
            foreach (var group in grouping)
            {
                if (string.IsNullOrWhiteSpace(group.Key))
                {
                    continue;
                }

                await helperFunc(group.Key, group.ToList());
            }

            foreach (var stamp in missingStamps)
            {
                await helperFunc(stamp.Name, new List<T>());
            }
        }
    }
}
