// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Stats;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Tracing;

// disable 'Missing XML comment for publicly visible type' warnings.
#pragma warning disable 1591
#pragma warning disable SA1600 // Elements must be documented

namespace BuildXL.Cache.ContentStore.Distributed.Tracing
{
    /// <summary>
    /// Tracer for RedisLocationBuildXL.ContentStore.
    /// </summary>
    public class RedisContentLocationStoreTracer : Tracer
    {
        public const string Component = "RedisContentLocationStore";
        public const string CategoryName = "CloudStore " + Component;

        protected const string UpdateBulkCallName = "UpdateBulk";
        protected const string GetBulkCallName = "GetBulk";
        protected const string TrimBulkCallName = "TrimBulk";
        protected const string TrimOrGetLastAccessTimeCallName = "TrimOrGetLastAccessTime";
        protected const string TrimBulkLocalCallName = "TrimBulkLocal";
        protected const string TouchBulkCallName = "TouchBulk";

        protected readonly Collection<CallCounter> CallCounters = new Collection<CallCounter>();
        private readonly CallCounter _updateBulkCallCounter;
        private readonly CallCounter _getBulkCallCounter;
        private readonly CallCounter _trimBulkCallCounter;
        private readonly CallCounter _trimOrGetLastAccessTimeCallCounter;
        private readonly CallCounter _trimBulkLocalCallCounter;
        private readonly CallCounter _touchBulkCallCounter;

        private const int DefaultArgsPerLog = 100;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisContentLocationStoreTracer"/> class.
        /// </summary>
        public RedisContentLocationStoreTracer(string name)
            : base(name)
        {
            Contract.Requires(name != null);

            CallCounters.Add(_updateBulkCallCounter = new CallCounter(UpdateBulkCallName));
            CallCounters.Add(_getBulkCallCounter = new CallCounter(GetBulkCallName));
            CallCounters.Add(_trimBulkCallCounter = new CallCounter(TrimBulkCallName));
            CallCounters.Add(_trimOrGetLastAccessTimeCallCounter = new CallCounter(TrimOrGetLastAccessTimeCallName));
            CallCounters.Add(_trimBulkLocalCallCounter = new CallCounter(TrimBulkLocalCallName));
            CallCounters.Add(_touchBulkCallCounter = new CallCounter(TouchBulkCallName));
        }

        public virtual CounterSet GetCounters()
        {
            var counterSet = new CounterSet();

            var callsCounterSet = new CounterSet();

            foreach (var callCounter in CallCounters)
            {
                callCounter.AppendTo(callsCounterSet);
            }

            return callsCounterSet.Merge(counterSet);
        }

        public void UpdateBulkStart()
        {
            _updateBulkCallCounter.Started();
        }

        public void UpdateBulkStop(Context context, ValueTuple<IReadOnlyList<ContentHashWithSizeAndLocations>, MachineId?> input, ResultBase result)
        {
            if (context.IsEnabled)
            {
                var contentHashes = string.Join(",", input.Item1);
                var machineId = input.Item2;
                TracerOperationFinished(context, result, $"{Name}.{UpdateBulkCallName}({input.Item1.Count}) stop {result.DurationMs}ms machineId=[{machineId}] input=[{contentHashes}] result=[{result}]");
            }

            _updateBulkCallCounter.Completed(result.Duration.Ticks);
        }

        public void TouchBulkStart()
        {
            _touchBulkCallCounter.Started();
        }

        public void TouchBulkStop(Context context, ValueTuple<IReadOnlyList<ContentHashWithSize>, MachineId?> input, ResultBase result)
        {
            if (context.IsEnabled)
            {
                // Using `ToString()` explicitly is strange, but without it logs show "BuildXL.Cache.ContentStore.Hashing.ContentHashWithSize" for each hash
                var contentHashes = string.Join(",", input.Item1.Select(hash => hash.ToString()));
                var machineId = input.Item2;
                TracerOperationFinished(context, result, $"{Name}.{TouchBulkCallName}({input.Item1.Count}) stop {result.DurationMs}ms machineId=[{machineId}] input=[{contentHashes}] result=[{result}]");
            }

            _touchBulkCallCounter.Completed(result.Duration.Ticks);
        }

        public void GetBulkStart()
        {
            _getBulkCallCounter.Started();
        }

        public void GetBulkStop(Context context, IReadOnlyList<ContentHash> input, GetBulkLocationsResult result)
        {
            if (context.IsEnabled)
            {
                string stringResult = result.Succeeded ? string.Join(",", result.ContentHashesInfo) : result.ErrorMessage;
                TracerOperationFinished(context, result, $"{Name}.{GetBulkCallName}({input.Count}) stop {result.DurationMs}ms result=[{stringResult}]");
            }

            _getBulkCallCounter.Completed(result.Duration.Ticks);
        }

        public void TrimBulkRemoteStart()
        {
            _trimBulkCallCounter.Started();
        }

        public void TrimBulkRemoteStop(Context context, IReadOnlyList<ContentHashAndLocations> input, ResultBase result)
        {
            if (context.IsEnabled)
            {
                var stringInput = string.Join(",", input);
                TracerOperationFinished(context, result, $"{Name}.{TrimBulkCallName}({input.Count}) stop {result.DurationMs}ms input=[{stringInput}] result=[{result}]");
            }

            _trimBulkCallCounter.Completed(result.Duration.Ticks);
        }

        public void TrimBulkLocalStart(Context context, IReadOnlyList<ContentHash> contentHashes, MachineId? machineId)
        {
            TraceOperationStarted(context, $"machineId=[{machineId}] count=[{contentHashes.Count}]");

            foreach (var hashBatch in contentHashes.GetPages(DefaultArgsPerLog))
            {
                context.Debug($"{nameof(TrimBulkLocalStart)} {string.Join(",", hashBatch)}");
            }

            _trimBulkLocalCallCounter.Started();
        }

        public void TrimBulkLocalStop(Context context, BoolResult result)
        {
            if (context.IsEnabled)
            {
                TracerOperationFinished(context, result, $"{Name}.{TrimBulkLocalCallName}() stop {result.DurationMs}ms result=[{result}]");
            }

            _trimBulkLocalCallCounter.Completed(result.Duration.Ticks);
        }

        public void TrimOrGetLastAccessTimeStart(Context context, IList<Tuple<ContentHashWithLastAccessTimeAndReplicaCount, bool>> contentHashesWithInfo, MachineId? machineId)
        {
            if (context.IsEnabled)
            {
                TraceOperationStarted(context, $"machineId=[{machineId}] count=[{contentHashesWithInfo.Count}]");

                foreach (var hashBatch in contentHashesWithInfo.GetPages(DefaultArgsPerLog))
                {
                    context.Debug($"{nameof(TrimOrGetLastAccessTimeStart)} {string.Join(",", hashBatch)}");
                }
            }

            _trimOrGetLastAccessTimeCallCounter.Started();
        }

        public void TrimOrGetLastAccessTimeStop(Context context, IList<Tuple<ContentHashWithLastAccessTimeAndReplicaCount, bool>> input, ObjectResult<IList<ContentHashWithLastAccessTimeAndReplicaCount>> result)
        {
            if (context.IsEnabled)
            {
                var stringResult = result.Succeeded ? string.Join(",", result.Data) : result.ErrorMessage;
                TracerOperationFinished(context, result, $"{Name}.{TrimOrGetLastAccessTimeCallName}({input.Count}) stop {result.DurationMs}ms result=[{stringResult}]");
            }

            _trimOrGetLastAccessTimeCallCounter.Completed(result.Duration.Ticks);
        }
    }
}
