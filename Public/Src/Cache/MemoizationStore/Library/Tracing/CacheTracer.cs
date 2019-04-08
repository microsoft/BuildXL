// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Stats;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

// disable 'Missing XML comment for publicly visible type' warnings.
#pragma warning disable 1591
#pragma warning disable SA1600 // Elements must be documented

namespace BuildXL.Cache.MemoizationStore.Tracing
{
    public class CacheTracer : Tracer
    {
        private readonly CallCounter _statsCounter = new CallCounter("CacheTracer.Stats");

        public CacheTracer(string name)
            : base(name)
        {
        }

        public override void GetStatsStop(Context context, GetStatsResult result)
        {
            _statsCounter.Completed(result.Duration.Ticks);
            base.GetStatsStop(context, result);
        }

        public void CreateReadOnlySessionStart(Context context, string name)
        {
            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.CreateReadOnlySession({name}) start");
            }
        }

        public void CreateReadOnlySessionStop(Context context, CreateSessionResult<IReadOnlyCacheSession> result)
        {
            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.CreateReadOnlySession() stop {result.DurationMs}ms result=[{result}]");
            }
        }

        public void CreateSessionStart(Context context, string name)
        {
            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.CreateSession({name}) start");
            }
        }

        public void CreateSessionStop(Context context, CreateSessionResult<ICacheSession> result)
        {
            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.CreateSession() stop {result.DurationMs}ms result=[{result}]");
            }
        }
    }
}
