using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.Monitor.App.Notifications;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal class PrintRule : IRule
    {
        public string Name => "Print Rule";

        private readonly INotifier _notifier;

        public PrintRule(INotifier notifier)
        {
            Contract.RequiresNotNull(notifier);

            _notifier = notifier;
        }

        public async Task Run()
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            _notifier.Emit(new Notification(SystemClock.Instance.UtcNow, ContentStore.Interfaces.Logging.Severity.Debug, "None!", "YEYE!"));
        }
    }
}
