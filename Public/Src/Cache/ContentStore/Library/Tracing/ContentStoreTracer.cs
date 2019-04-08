// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using System.Collections.ObjectModel;
using BuildXL.Cache.ContentStore.Stats;

// disable 'Missing XML comment for publicly visible type' warnings.
#pragma warning disable 1591
#pragma warning disable SA1600 // Elements must be documented

namespace BuildXL.Cache.ContentStore.Tracing
{
    public class ContentStoreTracer : Tracer
    {
        private readonly string _removeFromTrackerName = "RemoveFromTracker";

        private readonly Collection<CallCounter> _callCounters = new Collection<CallCounter>();
        private readonly CallCounter _removeFromTrackerCallCounter;

        public ContentStoreTracer(string name)
            : base(name)
        {
            _callCounters.Add(_removeFromTrackerCallCounter = new CallCounter(_removeFromTrackerName));
        }

        public CounterSet GetCounters()
        {
            var callsCounterSet = new CounterSet();
            foreach (var callCounter in _callCounters)
            {
                callCounter.AppendTo(callsCounterSet);
            }

            return callsCounterSet;
        }


        public void CreateReadOnlySessionStart(Context context, string name)
        {
            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.CreateReadOnlySession({name}) start");
            }
        }

        public void CreateReadOnlySessionStop(Context context, CreateSessionResult<IReadOnlyContentSession> result)
        {
            if (context.IsEnabled)
            {
                TracerOperationFinished(context, result, $"{Name}.CreateReadOnlySession() stop {result.DurationMs}ms result=[{result}]");
            }
        }

        public void CreateSessionStart(Context context, string name)
        {
            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.CreateSession({name}) start");
            }
        }

        public void CreateSessionStop(Context context, CreateSessionResult<IContentSession> result)
        {
            if (context.IsEnabled)
            {
                TracerOperationFinished(context, result, $"{Name}.CreateSession() stop {result.DurationMs}ms result=[{result}]");
            }
        }

        public void RemoveFromTrackerStart()
        {
            _removeFromTrackerCallCounter.Started();
        }

        public void RemoveFromTrackerStop(Context context, StructResult<long> result)
        {
            if (context.IsEnabled)
            {
                TracerOperationFinished(context, result, $"{Name}.{_removeFromTrackerName} stop {result.DurationMs}ms result=[{result}]");
            }

            _removeFromTrackerCallCounter.Completed(result.Duration.Ticks);
        }
    }
}
