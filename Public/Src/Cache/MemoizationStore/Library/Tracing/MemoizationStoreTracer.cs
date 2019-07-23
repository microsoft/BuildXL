// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stats;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

// disable 'Missing XML comment for publicly visible type' warnings.
#pragma warning disable 1591
#pragma warning disable SA1600 // Elements must be documented

namespace BuildXL.Cache.MemoizationStore.Tracing
{
    public class MemoizationStoreTracer : Tracer
    {
        private const string GetStatsCallName = "GetStats";
        private const string GetSelectorsCallName = "GetSelectors";
        private const string GetContentHashListCallName = "GetContentHashList";
        private const string AddOrGetContentHashListCallName = "AddOrGetContentHashList";
        private const string IncorporateStrongFingerprintsCallName = "IncorporateStringFingerprints";
        private const string StartContentHashListsCountName = "StartContentHashListsCount";
        private const string GetContentHashListMissCountName = "GetContentHashListMissCount";
        private const string GetContentHashListHitCountName = "GetContentHashListHitCount";
        private const string GetContentHashListErrorCountName = "GetContentHashListErrorCount";
        private const string AddOrGetContentHashListAddCountName = "AddOrGetContentHashListAddCount";
        private const string AddOrGetContentHashListGetCountName = "AddOrGetContentHashListGetCount";
        private const string AddOrGetContentHashListErrorCountName = "AddOrGetContentHashListErrorCount";
        private const string GetSelectorsEmptyCountName = "GetSelectorsEmptyCount";

        private readonly MemoizationStoreEventSource _eventSource = MemoizationStoreEventSource.Instance;

        protected readonly ICollection<CallCounter> CallCounters = new List<CallCounter>();
        private readonly CallCounter _getStatsCallCounter;
        private readonly CallCounter _getSelectorsCallCounter;
        private readonly CallCounter _getContentHashListCallCounter;
        private readonly CallCounter _addOrGetContentHashListCallCounter;
        private readonly CallCounter _incorporateStrongFingerprintsCallCounter;

        protected readonly ICollection<Counter> Counters = new List<Counter>();
        private readonly Counter _startContentHashListsCount;
        private readonly Counter _getContentHashListMissCounter;
        private readonly Counter _getContentHashListHitCounter;
        private readonly Counter _getContentHashListErrorCounter;
        private readonly Counter _addOrGetContentHashListAddCounter;
        private readonly Counter _addOrGetContentHashListGetCounter;
        private readonly Counter _addOrGetContentHashListErrorCounter;
        private readonly Counter _getSelectorsEmptyCounter;

        public MemoizationStoreTracer(ILogger logger, string name)
            : base(name)
        {
            Contract.Requires(logger != null);
            Contract.Requires(name != null);

            CallCounters.Add(_getStatsCallCounter = new CallCounter(GetStatsCallName));
            CallCounters.Add(_getSelectorsCallCounter = new CallCounter(GetSelectorsCallName));
            CallCounters.Add(_getContentHashListCallCounter = new CallCounter(GetContentHashListCallName));
            CallCounters.Add(_addOrGetContentHashListCallCounter = new CallCounter(AddOrGetContentHashListCallName));
            CallCounters.Add(_incorporateStrongFingerprintsCallCounter = new CallCounter(IncorporateStrongFingerprintsCallName));

            Counters.Add(_startContentHashListsCount = new Counter(StartContentHashListsCountName));
            Counters.Add(_getContentHashListMissCounter = new Counter(GetContentHashListMissCountName));
            Counters.Add(_getContentHashListHitCounter = new Counter(GetContentHashListHitCountName));
            Counters.Add(_getContentHashListErrorCounter = new Counter(GetContentHashListErrorCountName));
            Counters.Add(_addOrGetContentHashListAddCounter = new Counter(AddOrGetContentHashListAddCountName));
            Counters.Add(_addOrGetContentHashListGetCounter = new Counter(AddOrGetContentHashListGetCountName));
            Counters.Add(_addOrGetContentHashListErrorCounter = new Counter(AddOrGetContentHashListErrorCountName));
            Counters.Add(_getSelectorsEmptyCounter = new Counter(GetSelectorsEmptyCountName));
        }

        public CounterSet GetCounters()
        {
            var counterSet = new CounterSet();

            foreach (var counter in Counters)
            {
                counterSet.Add(counter.Name, counter.Value);
            }

            var callsCounterSet = new CounterSet();

            foreach (var callCounter in CallCounters)
            {
                callCounter.AppendTo(callsCounterSet);
            }

            return callsCounterSet.Merge(counterSet);
        }

        public override void Always(Context context, string message)
        {
            Trace(Severity.Always, context, message);
        }

        public void StartStats(Context context, long contentHashListCount, bool lruEnabled)
        {
            _startContentHashListsCount.Add(0);

            if (context.IsEnabled)
            {
                Debug(context, $"{Name} starting with ContentHashList count={contentHashListCount}, lruEnabled={lruEnabled}");
            }
        }

        public void EndStats(Context context, long contentHashListCount)
        {
            if (context.IsEnabled)
            {
                Debug(context, $"{Name} ending with ContentHashList count={contentHashListCount}");
            }
        }

        public override void StartupStart(Context context)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.StartupStart(context.Id.ToString());
            }

            base.StartupStart(context);
        }

        public override void StartupStop(Context context, BoolResult result)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.StartupStop(context.Id.ToString(), result.Succeeded, result.ErrorMessage);
            }

            base.StartupStop(context, result);
        }

        public override void ShutdownStart(Context context)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.ShutdownStart(context.Id.ToString());
            }

            base.ShutdownStart(context);
        }

        public override void ShutdownStop(Context context, BoolResult result)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.ShutdownStop(context.Id.ToString(), result.Succeeded, result.ErrorMessage);
            }

            base.ShutdownStop(context, result);
        }

        public override void GetStatsStart(Context context)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.GetStatsStart();
            }

            _getStatsCallCounter.Started();

            base.GetStatsStart(context);
        }

        public override void GetStatsStop(Context context, GetStatsResult result)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.GetStatsStop(result.Succeeded, result.ErrorMessage);
            }

            _getStatsCallCounter.Completed(result.Duration.Ticks);

            base.GetStatsStop(context, result);
        }

        public void GetSelectorsStart(Context context, Fingerprint fingerprint)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.GetSelectorsStart(context.Id.ToString(), fingerprint.ToHex());
            }

            _getSelectorsCallCounter.Started();

            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.GetSelectors({fingerprint}) start");
            }
        }

        public void GetSelectorsCount(Context context, Fingerprint fingerprint, long count)
        {
            Debug(context, $"Found {count} selectors for {fingerprint}");
            if (count == 0)
            {
                _getSelectorsEmptyCounter.Increment();
            }
        }

        public void GetSelectorsStop(Context context, TimeSpan duration)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.GetSelectorsStop(context.Id.ToString());
            }

            _getSelectorsCallCounter.Completed(duration.Ticks);

            if (!context.IsEnabled)
            {
                return;
            }

            Debug(context, $"{Name}.GetSelectors() stop {duration.TotalMilliseconds}ms");
        }

        public void GetContentHashListStart(Context context, StrongFingerprint fingerprint)
        {
            if (_eventSource.IsEnabled())
            {
                var weakHash = fingerprint.WeakFingerprint.ToString();
                var contentHash = fingerprint.Selector.ContentHash.ToString();
                var outputBytes = fingerprint.Selector.Output?.Length ?? 0;
                _eventSource.GetContentHashListStart(context.Id.ToString(), weakHash, contentHash, outputBytes);
            }

            _getContentHashListCallCounter.Started();

            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.GetContentHashList({fingerprint}) start");
            }
        }

        public void GetContentHashListStop(Context context, GetContentHashListResult result)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.GetContentHashListStop(context.Id.ToString(), result.Succeeded, result.ErrorMessage);
            }

            _getContentHashListCallCounter.Completed(result.Duration.Ticks);

            if (result.Succeeded)
            {
                if (result.ContentHashListWithDeterminism.ContentHashList == null)
                {
                    _getContentHashListMissCounter.Increment();
                }
                else
                {
                    _getContentHashListHitCounter.Increment();
                }
            }
            else
            {
                _getContentHashListErrorCounter.Increment();
            }

            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.GetContentHashList() stop {result.Duration.TotalMilliseconds}ms result=[{result}]");
            }
        }

        public void AddOrGetContentHashListStart(Context context, StrongFingerprint fingerprint)
        {
            if (_eventSource.IsEnabled())
            {
                var weakHash = fingerprint.WeakFingerprint.ToString();
                var contentHash = fingerprint.Selector.ContentHash.ToString();
                var outputBytes = fingerprint.Selector.Output?.Length ?? 0;
                _eventSource.AddOrGetContentHashListStart(context.Id.ToString(), weakHash, contentHash, outputBytes);
            }

            _addOrGetContentHashListCallCounter.Started();

            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.AddOrGetContentHashList({fingerprint}) start");
            }
        }

        public void AddOrGetContentHashListStop(Context context, AddOrGetContentHashListResult result)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.AddOrGetContentHashListStop(context.Id.ToString(), (int)result.Code, result.ErrorMessage);
            }

            _addOrGetContentHashListCallCounter.Completed(result.Duration.Ticks);

            if (result.Succeeded)
            {
                if (result.ContentHashListWithDeterminism.ContentHashList == null)
                {
                    _addOrGetContentHashListAddCounter.Increment();
                }
                else
                {
                    _addOrGetContentHashListGetCounter.Increment();
                }
            }
            else
            {
                _addOrGetContentHashListErrorCounter.Increment();
            }

            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.AddOrGetContentHashList() stop {result.Duration.TotalMilliseconds}ms result=[{result}]");
            }
        }

        public void IncorporateStrongFingerprintsStart(Context context)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.IncorporateStrongFingerprintsStart(context.Id.ToString());
            }

            _incorporateStrongFingerprintsCallCounter.Started();

            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.IncorporateStrongFingerprints() start");
            }
        }

        public void IncorporateStrongFingerprintsStop(Context context, BoolResult result)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.IncorporateStrongFingerprintsStop(context.Id.ToString(), result.Succeeded, result.ErrorMessage);
            }

            _incorporateStrongFingerprintsCallCounter.Completed(result.Duration.Ticks);

            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.IncorporateStrongFingerprints() stop {result.Duration.TotalMilliseconds}ms result=[{result}]");
            }
        }

        public void TouchStart(Context context, int count)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.TouchStart(context.Id.ToString(), count);
            }

            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.Touch({count}) start");
            }
        }

        public void TouchStop(Context context, TimeSpan duration)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.TouchStop(context.Id.ToString());
            }

            if (!context.IsEnabled)
            {
                return;
            }

            Debug(context, $"{Name}.Touch() stop {duration.TotalMilliseconds}ms");
        }

        public void PurgeStart(Context context, int count)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.PurgeStart(context.Id.ToString(), count);
            }

            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.Purge({count}) start");
            }
        }

        public void PurgeStop(Context context, int count, TimeSpan duration)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.PurgeStop(context.Id.ToString());
            }

            if (!context.IsEnabled)
            {
                return;
            }

            Debug(context, $"{Name}.Purge({count}) stop {duration.TotalMilliseconds}ms");
        }

        private void Trace(Severity severity, Context context, string message)
        {
            var eventSourceEnabled = _eventSource.IsEnabled(EventLevel.Verbose, MemoizationStoreEventSource.Keywords.Message);
            var contextEnabled = context.IsEnabled;

            if (!eventSourceEnabled && !contextEnabled)
            {
                return;
            }

            if (eventSourceEnabled)
            {
                _eventSource.Message(context.Id.ToString(), (int)severity, message);
            }

            if (contextEnabled)
            {
                context.TraceMessage(severity, message);
            }
        }
    }
}
