using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.Monitor.App.Rules;
using Newtonsoft.Json;

namespace BuildXL.Cache.Monitor.App
{
    internal class Scheduler
    {
        public class Configuration
        {
            public bool PersistState { get; set; } = false;

            public string PersistStatePath { get; set; } = null;

            public bool PersistClearFailedEntriesOnLoad { get; set; } = false;

            public TimeSpan PollingPeriod { get; set; } = TimeSpan.FromSeconds(0.5);
        }

        /// <summary>
        /// Stores all necessary information to perform scheduling of rules.
        /// </summary>
        /// 
        /// WARNING(jubayard): whenever this class gets updated, <see cref="PersistableEntry"/> should also.
        private class Entry
        {
            public readonly object Lock = new object();

            public IRule Rule { get; }

            public TimeSpan PollingPeriod { get; }

            public bool Running { get; set; } = false;

            public DateTime LastRunTimeUtc { get; set; }

            public bool Failed { get; set; } = false;

            public Entry(IRule rule, TimeSpan pollingPeriod)
            {
                Contract.RequiresNotNull(rule);

                Rule = rule;
                PollingPeriod = pollingPeriod;
            }

            public bool ShouldRun(DateTime now) => !Failed && !Running && LastRunTimeUtc + PollingPeriod <= now;
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
            if (_configuration.PersistState)
            {
                LoadState();

                // Force a persist before the run begins, since we need to consolidate the two states.
                PersistState();
            }

            _logger.Debug("Starting to monitor");

            while (!cancellationToken.IsCancellationRequested)
            {
                var now = _clock.UtcNow;

                foreach (var kvp in _schedule)
                {
                    var rule = kvp.Value.Rule;
                    var entry = kvp.Value;

                    lock (entry.Lock)
                    {
                        if (!entry.ShouldRun(now))
                        {
                            continue;
                        }

                        entry.Running = true;
                    }

                    _ = Task.Run(async () =>
                      {
                          // NOTE(jubayard): make sure this runs in a different thread than the scheduler.
                          await Task.Yield();

                          Exception exception = null;
                          try
                          {
                              _logger.Debug($"Running rule `{rule.Identifier}`");
                              await rule.Run();
                          }
                          catch (Exception thrownException)
                          {
                              exception = thrownException;
                          }
                          finally
                          {
                              lock (entry.Lock)
                              {
                                  entry.LastRunTimeUtc = _clock.UtcNow;
                                  entry.Running = false;

                                  if (exception != null)
                                  {
                                      entry.Failed = true;
                                      _logger.Error($"Rule `{rule.Identifier}` threw an exception and has been disabled. Exception: {exception}");
                                  }
                                  else
                                  {
                                      entry.Failed = false;
                                      _logger.Debug($"Rule `{rule.Identifier}` finished running successfully @ `{entry.LastRunTimeUtc}`");
                                  }
                              }
                          }
                      });
                }

                if (_configuration.PersistState)
                {
                    PersistState();
                }

                await Task.Delay(_configuration.PollingPeriod);
            }
        }

        #region Persistance
        private struct PersistableEntry
        {
            public TimeSpan PollingPeriod;

            public bool Failed;

            public DateTime LastRunTimeUtc;

            public PersistableEntry(Entry entry) {
                lock (entry.Lock)
                {
                    PollingPeriod = entry.PollingPeriod;
                    Failed = entry.Failed;
                    LastRunTimeUtc = entry.LastRunTimeUtc;
                }
            }
        };

        private void LoadState()
        {
            ValidatePersistanceContract();

            if (!File.Exists(_configuration.PersistStatePath))
            {
                _logger.Warning($"No previous state available at `{_configuration.PersistStatePath}`");
                return;
            }

            _logger.Debug($"Loading previous state from `{_configuration.PersistStatePath}`");

            try
            {
                using var stream = File.OpenText(_configuration.PersistStatePath);
                var serializer = CreateSerializer();

                var state = (Dictionary<string, PersistableEntry>)serializer.Deserialize(stream, typeof(Dictionary<string, PersistableEntry>));
                foreach (var kvp in state)
                {
                    var ruleIdentifier = kvp.Key;
                    var storedEntry = kvp.Value;

                    if (!_schedule.TryGetValue(ruleIdentifier, out var entry))
                    {
                        _logger.Warning($"Rule `{ruleIdentifier}` was found in persisted state but not on the in-memory state. Skipping it");
                        continue;
                    }

                    Overwrite(ruleIdentifier, storedEntry, entry);
                }
            }
            catch (Exception exception)
            {
                _logger.Error($"Failed to load scheduler state from `{_configuration.PersistStatePath}`; running as if it didn't exist. Exception: {exception}");
            }

        }

        private void Overwrite(string ruleIdentifier, PersistableEntry storedEntry, Entry entry)
        {
            Contract.RequiresNotNullOrEmpty(ruleIdentifier);
            Contract.RequiresNotNull(entry);

            lock (entry.Lock)
            {
                Contract.Assert(!entry.Running);

                Contract.Assert(!entry.Failed);
                if (storedEntry.Failed)
                {
                    if (_configuration.PersistClearFailedEntriesOnLoad)
                    {
                        _logger.Debug($"Rule `{ruleIdentifier}` is disabled in the persisted state, but clearing is allowed. Clearing in-memory");
                    }
                    else
                    {
                        _logger.Warning($"Rule `{ruleIdentifier}` is disabled in the persisted state. Disabling in the in-memory state as well");
                        entry.Failed = false;
                    }
                }

                Contract.Assert(storedEntry.LastRunTimeUtc >= DateTime.MinValue);
                Contract.Assert(storedEntry.LastRunTimeUtc <= _clock.UtcNow);
                entry.LastRunTimeUtc = storedEntry.LastRunTimeUtc;

                if (entry.PollingPeriod != storedEntry.PollingPeriod)
                {
                    _logger.Warning($"Rule `{ruleIdentifier}` has a mismatch between persisted and in-memory polling periods (`{storedEntry.PollingPeriod}` vs `{entry.PollingPeriod}`). Using the in-memory one.");
                }
            }
        }

        private void PersistState()
        {
            ValidatePersistanceContract();

            using var stream = File.CreateText(_configuration.PersistStatePath);
            var state = CreateSerializer();

            state.Serialize(stream, _schedule.ToDictionary(kvp => kvp.Key, kvp => new PersistableEntry(kvp.Value)));
        }

        private void ValidatePersistanceContract()
        {
            Contract.Requires(_configuration.PersistState);
            Contract.RequiresNotNull(_configuration.PersistStatePath);
            Contract.Requires(_configuration.PersistStatePath.EndsWith(".json"));
        }

        private static JsonSerializer CreateSerializer()
        {
            return new JsonSerializer
            {
                Formatting = Formatting.Indented,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc
            };
        }
        #endregion
    }
}
