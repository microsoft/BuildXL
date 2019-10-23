using System;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.Monitor.App.Notifications
{
    internal class Notification
    {
        public DateTime DateTimeUtc { get; }

        public Severity Severity { get; }

        public string Stamp { get; }

        public string Message { get; }
    }
}
