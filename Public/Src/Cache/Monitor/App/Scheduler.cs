using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.Monitor.App.Rules;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BuildXL.Cache.Monitor.App
{
    internal class Scheduler : IDisposable
    {
        public class Configuration
        {
            public string PersistStatePath { get; set; } = null;

            public bool PersistClearFailedEntriesOnLoad { get; set; } = false;

            public TimeSpan PollingPeriod { get; set; } = TimeSpan.FromSeconds(0.5);

            public TimeSpan? RetryOnFailureAfter { get; set; } = TimeSpan.FromMinutes(5);

            public int MaximumConcurrency { get; set; } = 10;
        }

        private enum State
        {
            Waiting,
            Scheduled,
            Running,
            Failed,
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

            public State State { get; set; } = State.Waiting;

            public DateTime LastRunTimeUtc { get; set; }

            public Entry(IRule rule, TimeSpan pollingPeriod)
            {
                Contract.RequiresNotNull(rule);

                Rule = rule;
                PollingPeriod = pollingPeriod;
            }

            public bool ShouldSchedule(DateTime now, TimeSpan? retryOnFailureWaitTime = null)
            {
                if (State == State.Waiting && LastRunTimeUtc + PollingPeriod <= now)
                {
                    return true;
                }

                if (retryOnFailureWaitTime != null && State == State.Failed && LastRunTimeUtc + retryOnFailureWaitTime.Value <= now)
                {
                    return true;
                }

                return false;
            }
        };

        private readonly ILogger _logger;
        private readonly IClock _clock;
        private readonly Configuration _configuration;

        private readonly IDictionary<string, Entry> _schedule = new Dictionary<string, Entry>();

        private readonly SemaphoreSlim _runGate;

        public Scheduler(Configuration configuration, ILogger logger, IClock clock)
        {
            Contract.RequiresNotNull(configuration);
            Contract.RequiresNotNull(logger);
            Contract.RequiresNotNull(clock);

            _configuration = configuration;
            _logger = logger;
            _clock = clock;

            _runGate = new SemaphoreSlim(_configuration.MaximumConcurrency);
        }

        public void Add(IRule rule, TimeSpan pollingPeriod)
        {
            Contract.RequiresNotNull(rule);
            Contract.Requires(pollingPeriod > _configuration.PollingPeriod);

            _schedule[rule.Identifier] = new Entry(rule, pollingPeriod);
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            if (IsPersistanceEnabled())
            {
                LoadState(_configuration.PersistStatePath);

                // Persist before the run begins, store the reconciliation
                SaveState(_configuration.PersistStatePath);
            }

            _logger.Debug("Starting to monitor");

            Dictionary<State, int> oldStates = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = _clock.UtcNow;

                // Approximate count of rules in each state. They do not necessarily sum to the total, because rules
                // may change state as we are computing the dictionary.
                var states = new Dictionary<State, int>() {
                    { State.Running, 0 },
                    { State.Scheduled, 0 },
                    { State.Waiting, 0 },
                    { State.Failed, 0 },
                };

                foreach (var kvp in _schedule)
                {
                    var rule = kvp.Value.Rule;
                    var entry = kvp.Value;

                    lock (entry.Lock)
                    {
                        states[entry.State] += 1;

                        if (!entry.ShouldSchedule(now, retryOnFailureWaitTime: _configuration.RetryOnFailureAfter))
                        {
                            continue;
                        }

                        Contract.Assert(entry.State == State.Waiting);
                        entry.State = State.Scheduled;
                    }

                    _ = Task.Run(async () =>
                    {
                        // NOTE(jubayard): make sure this runs in a different thread than the scheduler.
                        await Task.Yield();
                        await RunRuleAsync(entry);
                    });
                }

                if (oldStates == null || states.Any(kvp => oldStates[kvp.Key] != kvp.Value))
                {
                    var statesString = string.Join(", ", states.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                    _logger.Debug($"Scheduler state: {statesString}");
                    oldStates = states;
                }

                if (IsPersistanceEnabled())
                {
                    SaveState(_configuration.PersistStatePath);
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(_configuration.PollingPeriod);
                }
            }

            // TODO(jubayard): probably wait until all rules are done?...
        }

        private async Task RunRuleAsync(Entry entry)
        {
            Contract.AssertNotNull(entry);

            var rule = entry.Rule;
            var stopwatch = new Stopwatch();
            var failed = false;
            Exception exception = null;
            try
            {
                await _runGate.WaitAsync();

                lock (entry.Lock)
                {
                    Contract.Assert(entry.State == State.Scheduled);
                    entry.State = State.Running;
                }

                _logger.Debug($"Running rule `{rule.Identifier}`. `{_runGate.CurrentCount}` more allowed to run");

                stopwatch.Restart();
                await rule.Run();
            }
            catch (Exception thrownException)
            {
                failed = true;
                exception = thrownException;
            }
            finally
            {
                stopwatch.Stop();

                _runGate.Release();

                lock (entry.Lock)
                {
                    entry.LastRunTimeUtc = _clock.UtcNow;

                    Contract.Assert(entry.State == State.Running);
                    entry.State = failed ? State.Failed : State.Waiting;


                    var logMessage = $"Rule `{rule.Identifier}` finished running @ `{entry.LastRunTimeUtc}`, took {stopwatch.Elapsed} to run";
                    if (!failed)
                    {
                        _logger.Debug(logMessage);
                    }
                    else
                    {
                        // NOTE(jubayard): for very long exceptions, the description overwhelms the buffer.
                        _logger.Flush();
                        _logger.Error($"{logMessage}. An exception has been caught and the rule has been disabled. Exception: {exception}");
                        _logger.Flush();
                    }
                }
            }
        }

        #region Persistance
        private bool IsPersistanceEnabled()
        {
            return !string.IsNullOrEmpty(_configuration.PersistStatePath) && _configuration.PersistStatePath.EndsWith(".json");
        }

        private struct PersistableEntry
        {
            public TimeSpan PollingPeriod;

            [JsonConverter(typeof(StringEnumConverter))]
            public State State;

            public DateTime LastRunTimeUtc;

            public PersistableEntry(Entry entry) {
                lock (entry.Lock)
                {
                    PollingPeriod = entry.PollingPeriod;
                    State = entry.State;
                    LastRunTimeUtc = entry.LastRunTimeUtc;
                }
            }
        };

        private void LoadState(string stateFilePath)
        {
            Contract.RequiresNotNullOrEmpty(stateFilePath);

            if (!File.Exists(stateFilePath))
            {
                _logger.Warning($"No previous state available at `{stateFilePath}`");
                return;
            }

            _logger.Debug($"Loading previous state from `{stateFilePath}`");

            try
            {
                using var stream = File.OpenText(stateFilePath);
                var serializer = CreateSerializer();

                var state = (Dictionary<string, PersistableEntry>)serializer.Deserialize(stream, typeof(Dictionary<string, PersistableEntry>));
                if (state is null)
                {
                    // Happens when the file is empty
                    return;
                }

                foreach (var kvp in state)
                {
                    var ruleIdentifier = kvp.Key;
                    var storedEntry = kvp.Value;

                    if (!_schedule.TryGetValue(ruleIdentifier, out var entry))
                    {
                        _logger.Warning($"Rule `{ruleIdentifier}` was found in persisted state but not on the in-memory state. Skipping it");
                        continue;
                    }

                    Reconcile(ruleIdentifier, storedEntry, entry);
                }
            }
            catch (Exception exception)
            {
                _logger.Error($"Failed to load scheduler state from `{stateFilePath}`; running as if it didn't exist. Exception: {exception}");
            }

        }

        private void Reconcile(string ruleIdentifier, PersistableEntry storedEntry, Entry entry)
        {
            Contract.RequiresNotNullOrEmpty(ruleIdentifier);
            Contract.RequiresNotNull(entry);

            lock (entry.Lock)
            {
                Contract.Assert(entry.State == State.Waiting);

                if (storedEntry.State == State.Failed)
                {
                    if (_configuration.PersistClearFailedEntriesOnLoad)
                    {
                        _logger.Debug($"Rule `{ruleIdentifier}` is disabled in the persisted state, but clearing is allowed. Clearing in-memory");

                        // Need to reset so as to run it and figure out if we should disable it ASAP
                        storedEntry.LastRunTimeUtc = DateTime.MinValue;
                    }
                    else
                    {
                        _logger.Warning($"Rule `{ruleIdentifier}` is disabled in the persisted state. Disabling in the in-memory state as well");
                        entry.State = State.Failed;
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

        private void SaveState(string stateFilePath)
        {
            Contract.RequiresNotNullOrEmpty(stateFilePath);

            using var stream = File.CreateText(stateFilePath);
            var serializer = CreateSerializer();

            var schedule = _schedule.ToDictionary(kvp => kvp.Key, kvp => new PersistableEntry(kvp.Value));
            serializer.Serialize(stream, schedule);
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

        #region IDisposable Support
        private bool _disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _runGate.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose() => Dispose(true);
        #endregion
    }
}
