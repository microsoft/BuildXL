using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BuildXL.Cache.Monitor.App.Notifications
{

    internal class Notification
    {
        public DateTime PreciseTimeStamp { get; }

        public Severity Severity { get; }

        public string SeverityFriendly => Severity.ToString();

        [JsonConverter(typeof(StringEnumConverter))]
        public Environment Environment { get; }

        public string Stamp { get; }

        public string Message { get; }

        public Notification(DateTime preciseTimeStamp, Severity severity, Environment environment, string stamp, string message)
        {
            Contract.RequiresNotNullOrEmpty(message);

            PreciseTimeStamp = preciseTimeStamp;
            Severity = severity;
            Environment = environment;
            Stamp = stamp;
            Message = message;
        }
    }
}
