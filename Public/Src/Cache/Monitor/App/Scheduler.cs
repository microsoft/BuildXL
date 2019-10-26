using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.Monitor.App.Rules;

namespace BuildXL.Cache.Monitor.App
{
    internal class Scheduler
    {
        public class Settings
        {
            public TimeSpan PollingPeriod { get; set; } = TimeSpan.FromSeconds(0.5);
        }

        private class Entry
        {
            public DateTime LastRunTimeUtc { get; set; } = DateTime.MinValue;

            public int Enabled = 1;

            public TimeSpan PollingPeriod { get; set; }
        };

        private readonly ILogger _logger;
        private readonly IClock _clock;
        private readonly Settings _settings;

        private readonly ConcurrentDictionary<IRule, Entry> _schedule = new ConcurrentDictionary<IRule, Entry>();

        public Scheduler(Settings settings, ILogger logger, IClock clock)
        {
            Contract.RequiresNotNull(settings);
            Contract.RequiresNotNull(logger);
            Contract.RequiresNotNull(clock);

            _settings = settings;
            _logger = logger;
            _clock = clock;
        }

        public void Add(IRule rule, TimeSpan pollingPeriod)
        {
            Contract.RequiresNotNull(rule);
            Contract.Requires(pollingPeriod > _settings.PollingPeriod);

            _schedule[rule] = new Entry() {
                PollingPeriod = pollingPeriod,
            };
        }

        public async Task RunAsync(CancellationToken cancellationToken = default) {
            _logger.Debug("Starting to monitor");

            while (!cancellationToken.IsCancellationRequested)
            {
                var tasks = new List<Task>();

                foreach (var kvp in _schedule)
                {
                    var rule = kvp.Key;
                    var entry = kvp.Value;

                    if (!ShouldRun(entry))
                    {
                        continue;
                    }

                    Interlocked.Exchange(ref entry.Enabled, 0);

                    _ = Task.Run(async () =>
                      {
                          try
                          {
                              _logger.Debug($"Running rule `{rule.Name}`");
                              await rule.Run();

                              Interlocked.Exchange(ref entry.Enabled, 1);
                              _logger.Debug($"Rule `{rule.Name}` finished running successfully");
                          }
                          catch (Exception exception)
                          {
                              _logger.Error($"Rule `{rule.Name}` threw an exception and has been disabled. Exception: {exception}");
                          }
                      });
                }

                await Task.Delay(_settings.PollingPeriod);
            }
        }

        private bool ShouldRun(Entry entry)
        {
            return entry.Enabled == 1 || entry.LastRunTimeUtc + entry.PollingPeriod >= _clock.UtcNow;
        }
    }
}
