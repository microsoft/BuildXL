using System;
using System.Data;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Rules;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BuildXL.Cache.Monitor.App.Notifications
{

    internal class Notification
    {
        /// <summary>
        /// Name of the rule that created the notification.
        /// </summary>
        public string RuleIdentifier { get; }

        /// <summary>
        /// When the rule that emitted the notification object was triggered to run.
        /// </summary>
        public DateTime RuleRunTimeUtc { get; }

        /// <summary>
        /// When the notification object was created.
        /// </summary>
        public DateTime CreationTimeUtc { get; }

        /// <summary>
        /// When the event that triggered the notification happened.
        /// </summary>
        public DateTime EventTimeUtc { get; }

        public Severity Severity { get; }

        public string SeverityFriendly => Severity.ToString();

        [JsonConverter(typeof(StringEnumConverter))]
        public Env Environment { get; }

        public string Stamp { get; }

        public string Message { get; }

        public string Summary { get; }

        public Notification(string rule, DateTime ruleRunTimeUtc, DateTime creationTimeUtc, DateTime eventTimeUtc, Severity severity, Env environment, string stamp, string message, string summary = null)
        {
            Contract.RequiresNotNullOrEmpty(rule);
            Contract.RequiresNotNullOrEmpty(message);

            RuleIdentifier = rule;
            RuleRunTimeUtc = ruleRunTimeUtc;

            CreationTimeUtc = creationTimeUtc;
            EventTimeUtc = eventTimeUtc;
            Severity = severity;
            Environment = environment;
            Stamp = stamp;
            Message = message;
            Summary = summary;
        }
    }
}
