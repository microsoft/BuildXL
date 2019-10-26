using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.Monitor.App.Notifications
{
    internal class Notification
    {
        public DateTime PreciseTimeStamp { get; }

        public Severity Severity { get; }

        public string SeverityFriendly => Severity.ToString();

        public string Stamp { get; }

        public string Message { get; }

        public Notification(DateTime preciseTimeStamp, Severity severity, string stamp, string message)
        {
            Contract.RequiresNotNullOrEmpty(message);

            PreciseTimeStamp = preciseTimeStamp;
            Severity = severity;
            Stamp = stamp;
            Message = message;
        }
    }
}
