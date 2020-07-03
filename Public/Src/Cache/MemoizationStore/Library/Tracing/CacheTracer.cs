// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stats;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

// disable 'Missing XML comment for publicly visible type' warnings.
#pragma warning disable 1591
#pragma warning disable SA1600 // Elements must be documented

namespace BuildXL.Cache.MemoizationStore.Tracing
{
    public class CacheTracer : Tracer
    {
        private readonly CallCounter _statsCounter = new CallCounter("CacheTracer.Stats");
        private readonly CallCounter _sessionCounter = new CallCounter("CacheTracer.CreateSession");
        private readonly CallCounter _readSessionCounter = new CallCounter("CacheTracer.CreateReadOnlySession");

        public CacheTracer(string name)
            : base(name)
        {
        }

        public override void GetStatsStart(Context context)
        {
            _statsCounter.Started();
        }

        public override void GetStatsStop(Context context, GetStatsResult result)
        {
            _statsCounter.Completed(result.Duration.Ticks);
            base.GetStatsStop(context, result);
        }

        public void CreateReadOnlySessionStart(Context context, string name)
        {
            _readSessionCounter.Started();
            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.CreateReadOnlySession({name}) start");
            }
        }

        public void CreateReadOnlySessionStop(Context context, CreateSessionResult<IReadOnlyCacheSession> result)
        {
            _readSessionCounter.Completed(result.Duration.Ticks);
            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.CreateReadOnlySession() stop {result.DurationMs}ms result=[{result}]");
            }
        }

        public void CreateSessionStart(Context context, string name)
        {
            _sessionCounter.Started();
            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.CreateSession({name}) start");
            }
        }

        public void CreateSessionStop(Context context, CreateSessionResult<ICacheSession> result)
        {
            _sessionCounter.Completed(result.Duration.Ticks);
            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.CreateSession() stop {result.DurationMs}ms result=[{result}]");
            }
        }
    }
}
