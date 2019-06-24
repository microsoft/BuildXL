// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#pragma warning disable 1591
#pragma warning disable SA1600 // Elements must be documented

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stats;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Tracing
{
    public class ContentSessionTracer : Tracer
    {
        protected const string GetStatsCallName = "GetStats";
        protected const string PinCallName = "Pin";
        private const string PinBulkCallName = "PinBulk";
        protected const string OpenStreamCallName = "OpenStream";
        protected const string PlaceFileCallName = "PlaceFile";
        protected const string PutStreamCallName = "PutStream";
        protected const string PutFileCallName = "PutFile";
        private const string PlaceFileRetryCounterName = "PlaceFileRetryCount";
        private const string PinBulkFileCountCounterName = "PinBulkFileCount";

        protected readonly Collection<CallCounter> CallCounters = new Collection<CallCounter>();
        private readonly CallCounter _getStatsCallCounter;
        private readonly CallCounter _pinCallCounter;
        private readonly CallCounter _pinBulkCallCounter;
        private readonly CallCounter _openStreamCallCounter;
        private readonly CallCounter _placeFileCallCounter;
        private readonly CallCounter _putStreamCallCounter;
        private readonly CallCounter _putFileCallCounter;

        protected readonly Collection<Counter> Counters = new Collection<Counter>();
        private readonly Counter _placeFileRetryCounter;
        private readonly Counter _pinBulkFileCountCounter;

        public ContentSessionTracer(string name)
            : base(name)
        {
            CallCounters.Add(_getStatsCallCounter = new CallCounter(GetStatsCallName));
            CallCounters.Add(_pinCallCounter = new CallCounter(PinCallName));
            CallCounters.Add(_pinBulkCallCounter = new CallCounter(PinBulkCallName));
            CallCounters.Add(_openStreamCallCounter = new CallCounter(OpenStreamCallName));
            CallCounters.Add(_placeFileCallCounter = new CallCounter(PlaceFileCallName));
            CallCounters.Add(_putStreamCallCounter = new CallCounter(PutStreamCallName));
            CallCounters.Add(_putFileCallCounter = new CallCounter(PutFileCallName));

            Counters.Add(_placeFileRetryCounter = new Counter(PlaceFileRetryCounterName));
            Counters.Add(_pinBulkFileCountCounter = new Counter(PinBulkFileCountCounterName));
        }

        [Obsolete("Performance counters are not supported. Please use another constructor")]
        public ContentSessionTracer(string name, string categoryName)
            : this(name)
        {
        }

        public virtual CounterSet GetCounters()
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

        public override void GetStatsStart(Context context)
        {
            _getStatsCallCounter.Started();
            // Don't trace starts to reduce the amount of traces.
        }

        public override void GetStatsStop(Context context, GetStatsResult result)
        {
            _getStatsCallCounter.Completed(result.Duration.Ticks);
            base.GetStatsStop(context, result);
        }

        public virtual void PinStart(Context context)
        {
            // Don't trace starts to reduce the amount of traces.
            _pinCallCounter.Started();
        }

        public virtual void PinStop(Context context, ContentHash input, PinResult result)
        {
            if (context.IsEnabled)
            {
                TracerOperationFinished(context, result, $"{Name}.{PinCallName} stop {result.DurationMs}ms input=[{input}] result=[{result}]");
            }

            _pinCallCounter.Completed(result.Duration.Ticks);
        }

        public void PinBulkStart(Context context, IReadOnlyList<ContentHash> contentHashes)
        {
            // Don't trace starts to reduce the amount of traces.

            _pinBulkCallCounter.Started();
            _pinBulkFileCountCounter.Add(contentHashes.Count);
        }

        public void LogPinResults(Context context, IReadOnlyList<ContentHash> contentHashes, IReadOnlyList<PinResult> results)
        {
            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.{PinBulkCallName}({results.Count}) results:[{string.Join(",", results.Select((result, index) => $"{contentHashes[index]}={result}"))}]");
            }
        }

        public void PinBulkStop(Context context, TimeSpan duration)
        {
            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.{PinBulkCallName}() stop {duration.TotalMilliseconds}ms");
            }

            _pinBulkCallCounter.Completed(duration.Ticks);
        }

        public void PinBulkStop(Context context, TimeSpan duration, IReadOnlyList<ContentHash> contentHashes, IEnumerable<Indexed<PinResult>> results, Exception error)
        {
            if (context.IsEnabled)
            {
                const string Success = "Success", Error = "Error", Canceled = "Canceled";
                var pinStatus = error == null ? Success : (error.IsPinContextObjectDisposedException() ? Canceled : Error);

                int count = contentHashes.Count;
                Debug(context, $"{Name}.{PinBulkCallName}() stop by {duration.TotalMilliseconds}ms for {count} hash(es). Result={pinStatus}.");

                if (pinStatus == Success)
                {
                    // Successful case
                    TraceBulk(
                        $"{Name}.{PinBulkCallName}() stop by {duration.TotalMilliseconds}ms for {count} hash(es). Result={pinStatus}. ",
                        results.Select((result, index) => (result, hash: contentHashes[index])),
                        contentHashes.Count,
                        itemPrinter: tpl => $"{tpl.hash}={tpl.result.Item}",
                        printAction: message => Debug(context, message));
                }
                else if (pinStatus == Error)
                {
                    // An actual failure case.
                    this.Error(context, $"{Name}.{PinBulkCallName}() failed by {duration.TotalMilliseconds}ms for {count} hash(es). Error={error}");

                    TraceBulk(
                        $"{Name}.{PinBulkCallName}() failed for hashes",
                        results.Select((result, index) => (result, hash: contentHashes[index])),
                        contentHashes.Count,
                        itemPrinter: tpl => $"{tpl.hash}={tpl.result.Item}",
                        printAction: message => Debug(context, message));
                }

                // Don't have to print anything special for Canceled case. General message should be enough.
            }

            _pinBulkCallCounter.Completed(duration.Ticks);
        }

        public virtual void OpenStreamStart(Context context, ContentHash contentHash)
        {
            // Don't trace starts to reduce the amount of traces.
            _openStreamCallCounter.Started();
        }

        public virtual void OpenStreamStop(Context context, OpenStreamResult result)
        {
            if (context.IsEnabled)
            {
                TracerOperationFinished(context, result, $"{Name}.{OpenStreamCallName} stop {result.DurationMs}ms result=[{result}]");
            }

            _openStreamCallCounter.Completed(result.Duration.Ticks);
        }

        public virtual void PlaceFileStart(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode)
        {
            // Don't trace starts to reduce the amount of traces.
            _placeFileCallCounter.Started();
        }

        public void PlaceFileRetry(Context context, int retry)
        {
            if (retry <= 0)
            {
                return;
            }

            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.{PlaceFileCallName}() retry #{retry}");
            }

            _placeFileRetryCounter.Increment();
        }

        public virtual void PlaceFileStop(Context context, ContentHash input, PlaceFileResult result)
        {
            if (context.IsEnabled)
            {
                TracerOperationFinished(context, result, $"{Name}.{PlaceFileCallName} stop {result.DurationMs}ms input=[{input}] result=[{result}]");
            }

            _placeFileCallCounter.Completed(result.Duration.Ticks);
        }

        public virtual void PutFileStart(Context context, AbsolutePath path, FileRealizationMode mode, HashType hashType, bool trusted)
        {
            // Don't trace starts to reduce the amount of traces.
            _putFileCallCounter.Started();
        }

        public virtual void PutFileStart(Context context, AbsolutePath path, FileRealizationMode mode, ContentHash contentHash, bool trusted)
        {
            // Don't trace starts to reduce the amount of traces.
            _putFileCallCounter.Started();
        }

        public virtual void PutFileStop(Context context, PutResult result, bool trusted)
        {
            if (context.IsEnabled)
            {
                TracerOperationFinished(context, result, $"{Name}.{PutFileCallName} stop {result.DurationMs}ms result=[{result}] trusted={trusted}");
            }

            _putFileCallCounter.Completed(result.Duration.Ticks);
        }

        public virtual void PutStreamStart(Context context, HashType hashType)
        {
            // Don't trace starts to reduce the amount of traces.
            _putStreamCallCounter.Started();
        }

        public virtual void PutStreamStart(Context context, ContentHash contentHash)
        {
            // Don't trace starts to reduce the amount of traces.
            _putStreamCallCounter.Started();
        }

        public virtual void PutStreamStop(Context context, PutResult result)
        {
            if (context.IsEnabled)
            {
                TracerOperationFinished(context, result, $"{Name}.{PutStreamCallName} stop {result.DurationMs}ms result=[{result}]");
            }

            _putStreamCallCounter.Completed(result.Duration.Ticks);
        }
    }
}
