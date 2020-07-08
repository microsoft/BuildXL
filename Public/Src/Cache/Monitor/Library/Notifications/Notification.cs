// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BuildXL.Cache.Monitor.App.Notifications
{
    public class Notification
    {
        /// <summary>
        /// Name of the rule that created the notification.
        /// </summary>
        public string RuleIdentifier { get; }

        /// <summary>
        /// Guid uniquely identifying the run that produced this notification.
        /// </summary>
        public Guid RuleRunGuid { get; }

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

        /// <summary>
        /// The notification bucket is an arbitrary string, unique per-rule, that's expected to be common across all
        /// alerts emitted for the same reason.
        /// 
        /// For example, if alert A happens because a machine ran out of disk, the bucket should be "OutOfDisk" or
        /// something similar. Then, it is easy to find all alerts triggered by the same rule for the same reason. This
        /// help disaggregate, search, and for query purposes.
        /// </summary>
        public string Bucket { get; }

        public Severity Severity { get; }

        public string SeverityFriendly => Severity.ToString();

        [JsonConverter(typeof(StringEnumConverter))]
        public CloudBuildEnvironment Environment { get; }

        public string Stamp { get; }

        public string Message { get; }

        public string? Summary { get; }

        public Notification(string ruleIdentifier, Guid ruleRunGuid, DateTime ruleRunTimeUtc, DateTime creationTimeUtc, DateTime eventTimeUtc, string bucket, Severity severity, CloudBuildEnvironment environment, string stamp, string message, string? summary = null)
        {
            RuleIdentifier = ruleIdentifier;
            RuleRunGuid = ruleRunGuid;
            RuleRunTimeUtc = ruleRunTimeUtc;

            CreationTimeUtc = creationTimeUtc;
            EventTimeUtc = eventTimeUtc;
            Bucket = bucket;
            Severity = severity;
            Environment = environment;
            Stamp = stamp;
            Message = message;
            Summary = summary;
        }
    }
}
