using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.Monitor.App.Rules;
using Microsoft.WindowsAzure.Storage.Blob.Protocol;

namespace BuildXL.Cache.Monitor.App
{
    internal class Scheduler
    {
        public class Configuration
        {
            public TimeSpan PollingPeriod { get; set; } = TimeSpan.FromSeconds(0.5);
        }

        private class Entry
        {
            public IRule Rule { get; set; }

            public TimeSpan PollingPeriod { get; set; }

            private int _binaryEnabled = 1;

            private long _binaryLastRunTimeUtc = DateTime.MinValue.ToBinary();

            public bool Enabled
            {
                get => _binaryEnabled == 1;
                set => Interlocked.Exchange(ref _binaryEnabled, value ? 1 : 0);
            }

            public DateTime LastRunTimeUtc
            {
                get => DateTime.FromBinary(_binaryLastRunTimeUtc);
                set => Interlocked.Exchange(ref _binaryLastRunTimeUtc, value.ToBinary());
            }

            public Entry(IRule rule, TimeSpan pollingPeriod)
            {
                Contract.RequiresNotNull(rule);

                Rule = rule;
                PollingPeriod = pollingPeriod;
            }
        };

        private readonly ILogger _logger;
        private readonly IClock _clock;
        private readonly Configuration _configuration;

        private readonly IDictionary<string, Entry> _schedule = new Dictionary<string, Entry>();

        public Scheduler(Configuration configuration, ILogger logger, IClock clock)
        {
            Contract.RequiresNotNull(configuration);
            Contract.RequiresNotNull(logger);
            Contract.RequiresNotNull(clock);

            _configuration = configuration;
            _logger = logger;
            _clock = clock;
        }

        public void Add(IRule rule, TimeSpan pollingPeriod)
        {
            Contract.RequiresNotNull(rule);
            Contract.Requires(pollingPeriod > _configuration.PollingPeriod);

            _schedule[rule.Identifier] = new Entry(rule, pollingPeriod);
        }

        public async Task RunAsync(CancellationToken cancellationToken = default) {
            _logger.Debug("Starting to monitor");

            while (!cancellationToken.IsCancellationRequested)
            {
                var now = _clock.UtcNow;

                foreach (var kvp in _schedule)
                {
                    var rule = kvp.Value.Rule;
                    var entry = kvp.Value;

                    if (!ShouldRun(entry, now))
                    {
                        //_logger.Debug($"{rule.Identifier} - {entry.LastRunTimeUtc} - {now} - {entry.PollingPeriod}");
                        continue;
                    }

                    entry.Enabled = false;

                    _ = Task.Run(async () =>
                      {
                          try
                          {
                              _logger.Debug($"Running rule `{rule.Identifier}`");
                              await rule.Run();

                              // The fact that this is done atomically and in this order is the only reason we don't
                              // need to use locks.
                              entry.LastRunTimeUtc = _clock.UtcNow;
                              entry.Enabled = true;

                              _logger.Debug($"Rule `{rule.Identifier}` finished running successfully");
                          }
                          catch (Exception exception)
                          {
                              _logger.Error($"Rule `{rule.Identifier}` threw an exception and has been disabled. Exception: {exception}");
                          }
                      });
                }

                await Task.Delay(_configuration.PollingPeriod);
            }
        }

        private bool ShouldRun(Entry entry, DateTime now)
        {
            return entry.Enabled && entry.LastRunTimeUtc + entry.PollingPeriod <= now;
        }
    }
}
