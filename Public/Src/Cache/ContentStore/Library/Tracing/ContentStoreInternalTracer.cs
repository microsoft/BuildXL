// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stats;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.UtilitiesCore;

// disable 'Missing XML comment for publicly visible type' warnings.
#pragma warning disable 1591
#pragma warning disable SA1600 // Elements must be documented

namespace BuildXL.Cache.ContentStore.Tracing
{
    public class ContentStoreInternalTracer : ContentSessionTracer
    {
        private const string StartByteCountName = "StartByteCount";
        private const string StartFileCountName = "StartFileCount";

        private const string PutBytesCountName = "PutBytesCount";
        private const string PutFilesCountName = "PutFilesCount";
        private const string EvictBytesCountName = "EvictBytesCount";
        private const string EvictFilesCountName = "EvictFilesCount";

        private const string CreateHardLinkCallName = "CreateHardLink";
        private const string PlaceFileCopyCallName = "PlaceFileCopy";
        private const string ReconstructCallName = "Reconstruct";
        private const string ReconstructCallExceptionName = "ReconstructException";
        private const string EvictCallName = "Evict";
        private const string PurgeCallName = "Purge";
        private const string CalibrateCallName = "Calibrate";
        private const string HashContentFileCallName = "HashContentFile";
        private const string PutFileExistingHardlinkCallName = "PutFileExistingHardlink";
        private const string PutFileNewHardlinkCallName = "PutFileNewHardlink";
        private const string PutFileNewCopyCallName = "PutFileNewCopy";
        private const string PutContentInternalCallName = "PutContentInternal";
        private const string ApplyPermsCallName = "ApplyPerms";

        private readonly ContentStoreInternalEventSource _eventSource = ContentStoreInternalEventSource.Instance;

        private readonly CallCounter _createHardLinkCallCounter;
        private readonly CallCounter _placeFileCopyCallCounter;
        private readonly CallCounter _reconstructCallCounter;
        private readonly CallCounter _evictCallCounter;
        private readonly CallCounter _purgeCallCounter;
        private readonly CallCounter _calibrateCallCounter;
        private readonly CallCounter _hashContentFileCallCounter;
        private readonly CallCounter _putFileExistingHardlinkCallCounter;
        private readonly CallCounter _putFileNewHardlinkCallCounter;
        private readonly CallCounter _putFileNewCopyCallCounter;
        private readonly CallCounter _putContentInternalCallCounter;
        private readonly CallCounter _applyPermsCallCounter;

        private readonly List<Counter> _counters = new List<Counter>();
        private readonly Counter _startBytesCount;
        private readonly Counter _startFilesCount;
        private readonly Counter _putBytesCount;
        private readonly Counter _putFilesCount;
        private readonly Counter _evictBytesCount;
        private readonly Counter _evictFilesCount;
        private readonly Counter _reconstructCallExceptionCounter;

        public ContentStoreInternalTracer()
            : base(FileSystemContentStoreInternal.Component)
        {
            CallCounters.Add(_createHardLinkCallCounter = new CallCounter(CreateHardLinkCallName));
            CallCounters.Add(_placeFileCopyCallCounter = new CallCounter(PlaceFileCopyCallName));
            CallCounters.Add(_reconstructCallCounter = new CallCounter(ReconstructCallName));
            CallCounters.Add(_evictCallCounter = new CallCounter(EvictCallName));
            CallCounters.Add(_purgeCallCounter = new CallCounter(PurgeCallName));
            CallCounters.Add(_calibrateCallCounter = new CallCounter(CalibrateCallName));
            CallCounters.Add(_hashContentFileCallCounter = new CallCounter(HashContentFileCallName));
            CallCounters.Add(_putFileExistingHardlinkCallCounter = new CallCounter(PutFileExistingHardlinkCallName));
            CallCounters.Add(_putFileNewHardlinkCallCounter = new CallCounter(PutFileNewHardlinkCallName));
            CallCounters.Add(_putFileNewCopyCallCounter = new CallCounter(PutFileNewCopyCallName));
            CallCounters.Add(_putContentInternalCallCounter = new CallCounter(PutContentInternalCallName));
            CallCounters.Add(_applyPermsCallCounter = new CallCounter(ApplyPermsCallName));

            _counters.Add(_startBytesCount = new Counter(StartByteCountName));
            _counters.Add(_startFilesCount = new Counter(StartFileCountName));
            _counters.Add(_putBytesCount = new Counter(PutBytesCountName));
            _counters.Add(_putFilesCount = new Counter(PutFilesCountName));
            _counters.Add(_evictBytesCount = new Counter(EvictBytesCountName));
            _counters.Add(_evictFilesCount = new Counter(EvictFilesCountName));
            _counters.Add(_reconstructCallExceptionCounter = new Counter(ReconstructCallExceptionName));
        }

        public override CounterSet GetCounters()
        {
            var counterSet = new CounterSet();

            foreach (var counter in _counters)
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

        public void StartStats(Context context, long curBytesCount, long curFilesCount)
        {
            _startBytesCount.Add(curBytesCount);
            _startFilesCount.Add(curFilesCount);

            if (context.IsEnabled)
            {
                Info(context, $"{Name} starting with {curBytesCount / 1024 / 1024}MB, {curFilesCount} files");
            }
        }

        public void EndStats(Context context, long curBytesCount, long curFilesCount)
        {
            if (context.IsEnabled)
            {
                Info(context, $"{Name} ending with {curBytesCount / 1024 / 1024}MB, {curFilesCount} files");
            }
        }

        public void AddPutBytes(long value)
        {
            _putBytesCount.Add(value);
            _putFilesCount.Add(1);
        }

        public override void Always(Context context, string message)
        {
            Trace(Severity.Always, context, message);
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

            base.GetStatsStart(context);
        }

        public override void GetStatsStop(Context context, GetStatsResult result)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.GetStatsStop(result.Succeeded, result.ErrorMessage);
            }

            base.GetStatsStop(context, result);
        }

        public override void OpenStreamStart(Context context, ContentHash contentHash)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.OpenStreamStart(context.Id.ToString(), contentHash.ToString());
            }

            base.OpenStreamStart(context, contentHash);
        }

        public override void OpenStreamStop(Context context, OpenStreamResult result)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.OpenStreamStop(context.Id.ToString(), (int)result.Code, result.ErrorMessage);
            }

            base.OpenStreamStop(context, result);
        }

        public void PinStart(Context context, ContentHash contentHash)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.PinStart(context.Id.ToString(), contentHash.ToString());
            }

            PinStart(context);
        }

        public override void PinStop(Context context, ContentHash input, PinResult result)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.PinStop(context.Id.ToString(), (int)result.Code, result.ErrorMessage);
            }

            base.PinStop(context, input, result);
        }

        public override void PlaceFileStart(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.PlaceFileStart(
                    context.Id.ToString(), contentHash.ToString(), path.Path, (int)accessMode, (int)replacementMode, (int)realizationMode);
            }

            base.PlaceFileStart(context, contentHash, path, accessMode, replacementMode, realizationMode);
        }

        public override void PlaceFileStop(Context context, ContentHash input, PlaceFileResult result)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.PlaceFileStop(context.Id.ToString(), (int)result.Code, result.ErrorMessage);
            }

            base.PlaceFileStop(context, input, result);
        }

        public override void PutFileStart(Context context, AbsolutePath path, FileRealizationMode mode, HashType hashType, bool trusted)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.PutFileStart(context.Id.ToString(), path.Path, (int)mode, hashType.ToString());
            }

            base.PutFileStart(context, path, mode, hashType, trusted);
        }

        public override void PutFileStart(Context context, AbsolutePath path, FileRealizationMode mode, ContentHash contentHash, bool trusted)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.PutFileStart(context.Id.ToString(), path.Path, (int)mode, contentHash.ToString());
            }

            base.PutFileStart(context, path, mode, contentHash, trusted);
        }

        public override void PutFileStop(Context context, PutResult result, bool trusted)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.PutFileStop(
                    context.Id.ToString(), result.Succeeded, result.ErrorMessage, result.ContentHash.ToString());
            }

            base.PutFileStop(context, result, trusted);
        }

        public override void PutStreamStart(Context context, HashType hashType)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.PutStreamStart(context.Id.ToString(), hashType.ToString());
            }

            base.PutStreamStart(context, hashType);
        }

        public override void PutStreamStart(Context context, ContentHash contentHash)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.PutStreamStart(context.Id.ToString(), contentHash.ToString());
            }

            base.PutStreamStart(context, contentHash);
        }

        public override void PutStreamStop(Context context, PutResult result)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.PutStreamStop(
                    context.Id.ToString(), result.Succeeded, result.ErrorMessage, result.ContentHash.ToString());
            }

            base.PutStreamStop(context, result);
        }

        public void CreateHardLink(
            Context context,
            CreateHardLinkResult result,
            AbsolutePath source,
            AbsolutePath destination,
            FileRealizationMode mode,
            bool replace,
            TimeSpan duration)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.CreateHardLink(context.Id.ToString(), (int)result, source.Path, destination.Path, (int)mode, replace);
            }

            _createHardLinkCallCounter.Completed(duration.Ticks);

            if (!context.IsEnabled)
            {
                return;
            }

            var ms = (long)duration.TotalMilliseconds;
            Debug(context, $"{Name}.CreateHardLink=[{result}] [{source}] to [{destination}]. mode=[{mode}] replaceExisting=[{replace}] {ms}ms");
        }

        public void PlaceFileCopy(Context context, AbsolutePath path, ContentHash contentHash, TimeSpan duration)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.PlaceFileCopy(context.Id.ToString(), path.Path, contentHash.ToString());
            }

            _placeFileCopyCallCounter.Completed(duration.Ticks);

            if (!context.IsEnabled)
            {
                return;
            }

            var ms = (long)duration.TotalMilliseconds;
            Debug(context, $"{Name}.PlaceFileCopy({path},{contentHash}) {ms}ms");
        }

        public void ReconstructDirectory(Context context, TimeSpan duration, long contentCount, long contentSize)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.ReconstructDirectory();
            }

            _reconstructCallCounter.Completed(duration.Ticks);

            if (!context.IsEnabled)
            {
                return;
            }

            var ms = (long)duration.TotalMilliseconds;
            Debug(context, $"{Name}.ReconstructDirectory() {ms}ms ContentCount=[{contentCount}] ContentSize=[{contentSize}]");
        }

        public void ReconstructDirectoryException(Context context, Exception exception)
        {
            _reconstructCallExceptionCounter.Increment();

            if (context.IsEnabled)
            {
                Error(context, exception, $"{Name}.ReconstructDirectory() failed");
            }
        }

        public void EvictStart(ContentHash contentHash)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.EvictStart(contentHash.ToString());
            }

            _evictCallCounter.Started();
        }

        public void EvictStop(Context context, ContentHash input, EvictResult result)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.EvictStop(result.Succeeded, result.EvictedSize, result.EvictedFiles, result.PinnedSize);
            }

            _evictCallCounter.Completed(result.Duration.Ticks);
            _evictBytesCount.Add(result.EvictedSize);
            _evictFilesCount.Add(result.EvictedFiles);

            if (context.IsEnabled)
            {
                TracerOperationFinished(context, result, $"{Name}.Evict() stop {result.DurationMs}ms input=[{input}] result=[{result}]");
            }
        }

        public void PurgeStart(Context context)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.PurgeStart();
            }

            _purgeCallCounter.Started();

            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.Purge start");
            }
        }

        public void PurgeStop(Context context, PurgeResult result)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.PurgeStop(result.Succeeded, result.ErrorMessage, result.EvictedSize, result.EvictedFiles, result.PinnedSize);
            }

            _purgeCallCounter.Completed(result.Duration.Ticks);

            if (context.IsEnabled)
            {
                TracerOperationFinished(context, result, $"{Name}.Purge stop {result.DurationMs}ms result=[{result}]");
            }
        }

        public void CalibrateStart(Context context)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.CalibrateStart();
            }

            _calibrateCallCounter.Started();

            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.Calibrate start");
            }
        }

        public void CalibrateStop(Context context, CalibrateResult result)
        {
            if (_eventSource.IsEnabled())
            {
                _eventSource.CalibrateStop(result.Succeeded, result.ErrorMessage, result.Size);
            }

            _calibrateCallCounter.Completed(result.Duration.Ticks);

            if (context.IsEnabled)
            {
                TracerOperationFinished(context, result, $"{Name}.Calibrate stop {result.DurationMs}ms result=[{result}]");
            }
        }

        public void HashContentFileStop(Context context, AbsolutePath path, TimeSpan duration)
        {
            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.{HashContentFileCallName}({path}) {duration.TotalMilliseconds}ms");
            }

            _hashContentFileCallCounter.Completed(duration.Ticks);
        }

        public void PutFileExistingHardLinkStart()
        {
            _putFileExistingHardlinkCallCounter.Started();
        }

        public void PutFileExistingHardLinkStop(TimeSpan duration)
        {
            _putFileExistingHardlinkCallCounter.Completed(duration.Ticks);
        }

        public void PutFileNewHardLinkStart()
        {
            _putFileNewHardlinkCallCounter.Started();
        }

        public void PutFileNewHardLinkStop(TimeSpan duration)
        {
            _putFileNewHardlinkCallCounter.Completed(duration.Ticks);
        }

        public void PutFileNewCopyStart()
        {
            _putFileNewCopyCallCounter.Started();
        }

        public void PutFileNewCopyStop(TimeSpan duration)
        {
            _putFileNewCopyCallCounter.Completed(duration.Ticks);
        }

        public void PutContentInternalStart()
        {
            _putContentInternalCallCounter.Started();
        }

        public void PutContentInternalStop(TimeSpan duration)
        {
            _putContentInternalCallCounter.Completed(duration.Ticks);
        }

        public void ApplyPermsStart()
        {
            _applyPermsCallCounter.Started();
        }

        public void ApplyPermsStop(TimeSpan duration)
        {
            _applyPermsCallCounter.Completed(duration.Ticks);
        }

        private void Trace(Severity severity, Context context, string message)
        {
            var eventSourceEnabled = _eventSource.IsEnabled(EventLevel.Verbose, ContentStoreInternalEventSource.Keywords.Message);
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
