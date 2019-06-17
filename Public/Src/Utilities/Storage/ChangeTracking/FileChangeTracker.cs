// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using BuildXL.Native.IO;
using BuildXL.Storage.ChangeJournalService;
using BuildXL.Storage.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using JetBrains.Annotations;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// State of a <see cref="FileChangeTracker" />.
    /// </summary>
    public enum FileChangeTrackingState
    {
        /// <summary>
        /// First iteration of change tracking: The set of relevant files is not known, so semantically 'everything' has changed.
        /// </summary>
        BuildingInitialChangeTrackingSet,

        /// <summary>
        /// Subsequent iteration of change tracking: The set of relevant files in the previous iteration is known, and may be queried for changes and updated.
        /// </summary>
        TrackingChanges,

        /// <summary>
        /// The tracker has encountered one ore more files which could not be tracked. A subsequent iteration will behave as if the first, in which
        /// the set of relevant files is unknown and so 'everything' has changed (see <see cref="BuildingInitialChangeTrackingSet"/>).
        /// </summary>
        DisabledSinceTrackingIsIncomplete,
    }

    /// <summary>
    /// Manages durable tracking of changes with a <see cref="FileChangeTrackingSet" /> over multiple iterations of some incremental procedure which produces and consumes files.
    /// The procedure must be able to operate in two modes:
    /// - Complete: Produce or consume all relevant files
    /// - Incremental: Produce or consume a subset of relevant files, based on change information.
    /// An initially created <see cref="FileChangeTracker" /> is in the <see cref="FileChangeTrackingState.BuildingInitialChangeTrackingSet" />;
    /// the caller must first establish the set of all interesting files via its Complete mode (this is the first iteration, so there is no previous set to find changes from).
    /// On subsequent iterations, the tracker is in the <see cref="FileChangeTrackingState.TrackingChanges" /> state: the calling procedure may choose to
    /// query the tracker for changes since the last iteration, and consume / update only change-relevant files via its Incremental mode (other files not touched since the prior iteration
    /// are retained, in case they change in the future).
    /// </summary>
    public sealed class FileChangeTracker : IFileChangeTrackingSubscriptionSource, IFileChangeTrackingObservable
    {
        /// <summary>
        /// Envelope for serialization
        /// </summary>
        public static readonly FileEnvelope FileEnvelope = new FileEnvelope(name: nameof(FileChangeTracker), version: 9);

        private int m_trackingStateValue;

        private readonly VolumeMap m_volumeMap;
        private readonly IChangeJournalAccessor m_journal;
        private FileChangeTrackingSet m_changeTrackingSet;
        private readonly LoggingContext m_loggingContext;

        private readonly List<IFileChangeTrackingObserver> m_observers = new List<IFileChangeTrackingObserver>(2);

        /// <nodoc/>
        public readonly CounterCollection<FileChangeTrackingCounter> Counters;

        /// <inheritdoc />
        public FileEnvelopeId FileEnvelopeId { get; }

        private readonly string m_buildEngineFingerprint;

        /// <summary>
        /// Current state of the tracker. The tracker may transition to the <see cref="FileChangeTrackingState.DisabledSinceTrackingIsIncomplete"/>
        /// state in the event of tracking failures.
        /// </summary>
        private FileChangeTrackingState TrackingState => (FileChangeTrackingState)Volatile.Read(ref m_trackingStateValue);

        /// <summary>
        /// Indicates if this change tracker can be queried for changes and updated.
        /// </summary>
        public bool IsTrackingChanges => TrackingState == FileChangeTrackingState.TrackingChanges;

        /// <summary>
        /// Indicates if this change tracker is the first iteration of change tracking.
        /// </summary>
        public bool IsBuildingInitialChangeTrackingSet => TrackingState == FileChangeTrackingState.BuildingInitialChangeTrackingSet;

        private bool IsDisabledOrNullTrackingSet => m_changeTrackingSet == null || TrackingState == FileChangeTrackingState.DisabledSinceTrackingIsIncomplete;

        /// <summary>
        /// Indicates if this change tracker has an updated set of tracked files (vs. creation or load time)
        /// or if it has advanced the checkpoints of one or more volume journals. In either case, the change tracking
        /// set should be re-persisted, if applicable, to avoid repeated work.
        /// </summary>
        /// <remarks>
        /// This is not allowed if in the tracker has become disabled (see <see cref="TrackingState"/>).
        /// </remarks>
        public bool HasNewFileOrCheckpointData
        {
            get
            {
                Contract.Requires(!IsDisabledOrNullTrackingSet);
                return m_changeTrackingSet.HasNewFileOrCheckpointData;
            }
        }

        private FileChangeTracker(
            LoggingContext loggingContext,
            FileEnvelopeId fileEnvelopeId,
            FileChangeTrackingState initialState,
            VolumeMap volumeMap,
            IChangeJournalAccessor journal,
            FileChangeTrackingSet currentChangeTrackingSet,
            string buildEngineFingerprint)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(fileEnvelopeId.IsValid);
            Contract.Requires(
                initialState == FileChangeTrackingState.BuildingInitialChangeTrackingSet || initialState == FileChangeTrackingState.TrackingChanges);
            Contract.Requires(volumeMap != null);
            Contract.Requires(journal != null);
            Contract.Requires(currentChangeTrackingSet != null);

            FileEnvelopeId = fileEnvelopeId;
            m_loggingContext = loggingContext;
            m_volumeMap = volumeMap;
            m_journal = journal;
            m_changeTrackingSet = currentChangeTrackingSet;
            m_trackingStateValue = (int)initialState;
            Counters = m_changeTrackingSet.Counters;
            m_buildEngineFingerprint = buildEngineFingerprint;
        }

        private FileChangeTracker(LoggingContext loggingContext)
        {
            Contract.Requires(loggingContext != null);
            Contract.Ensures(TrackingState == FileChangeTrackingState.DisabledSinceTrackingIsIncomplete);

            m_loggingContext = loggingContext;
            m_volumeMap = null;
            m_journal = null;
            m_changeTrackingSet = null;
            m_trackingStateValue = (int)FileChangeTrackingState.DisabledSinceTrackingIsIncomplete;
            Counters = new CounterCollection<FileChangeTrackingCounter>();
            m_buildEngineFingerprint = null;
        }

        #region Start, resume, or load tracker

        /// <summary>
        /// Creates a new change tracker in the <see cref="FileChangeTrackingState.BuildingInitialChangeTrackingSet"/> state.
        /// The caller may then add tracking for full set of files of interest, for later re-use by
        /// <see cref="ResumeTrackingChanges(LoggingContext,BuildXL.Utilities.FileEnvelopeId,VolumeMap,IChangeJournalAccessor,FileChangeTrackingSet,string)"/>.
        /// </summary>
        public static FileChangeTracker StartTrackingChanges(
            LoggingContext loggingContext, 
            VolumeMap volumeMap, 
            IChangeJournalAccessor journal, 
            string buildEngineFingerprint,
            FileEnvelopeId? correlatedId = default)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(volumeMap != null);
            Contract.Requires(journal != null);
            Contract.Ensures(Contract.Result<FileChangeTracker>().TrackingState == FileChangeTrackingState.BuildingInitialChangeTrackingSet);

            return new FileChangeTracker(
                loggingContext,
                correlatedId ?? FileEnvelopeId.Create(),
                FileChangeTrackingState.BuildingInitialChangeTrackingSet,
                volumeMap,
                journal,
                FileChangeTrackingSet.CreateForAllCapableVolumes(loggingContext, volumeMap, journal),
                buildEngineFingerprint);
        }

        /// <summary>
        /// Creates a new change tracker in the <see cref="FileChangeTrackingState.TrackingChanges"/> state.
        /// The caller may query for new changes since the tracking set was last checkpointed and persisted, and may track additional files.
        /// </summary>
        private static FileChangeTracker ResumeTrackingChanges(
            LoggingContext loggingContext,
            FileEnvelopeId fileEnvelopeId,
            VolumeMap volumeMap,
            IChangeJournalAccessor journal,
            FileChangeTrackingSet previousChangeTrackingSet,
            string buildEngineFingerprint)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(volumeMap != null);
            Contract.Requires(journal != null);
            Contract.Requires(previousChangeTrackingSet != null);
            Contract.Requires(fileEnvelopeId.IsValid);
            Contract.Ensures(Contract.Result<FileChangeTracker>().TrackingState == FileChangeTrackingState.TrackingChanges);

            return new FileChangeTracker(
                loggingContext,
                fileEnvelopeId,
                FileChangeTrackingState.TrackingChanges,
                volumeMap,
                journal,
                previousChangeTrackingSet,
                buildEngineFingerprint);
        }

        /// <summary>
        /// Tries to load a <see cref="FileChangeTrackingSet"/> and resume tracking changes.
        /// If loading is successful (including matching the provided atomic save token), the returned tracker begins in the <see cref="FileChangeTrackingState.TrackingChanges"/> state.
        /// The caller may then query for new changes since the tracking set was last checkpointed and persisted, and may track additional files.
        /// Otherwise, a tracker with a new and empty change tracking set is created, initially in the <see cref="FileChangeTrackingState.BuildingInitialChangeTrackingSet"/> state.
        /// In that case, attempting to query changes will fail (the caller should fall back to doing complete rather than incremental work, thus populating the new change tracking set).
        /// </summary>
        /// <exception cref="BuildXLException">Thrown in the event of an I/O error other than the given path being absent.</exception>
        public static LoadingTrackerResult ResumeOrRestartTrackingChanges(
            LoggingContext loggingContext,
            VolumeMap volumeMap,
            IChangeJournalAccessor journal,
            string path,
            string buildEngineFingerprint,
            out FileChangeTracker tracker)
        {
            Contract.Requires(volumeMap != null);
            Contract.Requires(journal != null);
            Contract.Requires(path != null);
            Contract.Ensures(Contract.Result<LoadingTrackerResult>() != null);
            Contract.Ensures(Contract.ValueAtReturn(out tracker) != null);
            Contract.Ensures(
                (Contract.Result<LoadingTrackerResult>().Succeeded && Contract.ValueAtReturn(out tracker).IsTrackingChanges)
                || (!Contract.Result<LoadingTrackerResult>().Succeeded && Contract.ValueAtReturn(out tracker).TrackingState ==
                    FileChangeTrackingState.BuildingInitialChangeTrackingSet));

            using (var pm = BuildXL.Tracing.PerformanceMeasurement.StartWithoutStatistic(
                loggingContext,
                loggingContext1 => Logger.Log.StartLoadingChangeTracker(loggingContext1, path),
                loggingContext1 => Logger.Log.EndLoadingChangeTracker(loggingContext1)))
            {

                // Note that TryLoad may throw in the event of spooky I/O errors.
                var loadingTrackerResult = TryLoad(pm.LoggingContext, path, volumeMap, journal, buildEngineFingerprint);

                if (loadingTrackerResult.Succeeded)
                {
                    Contract.Assert(loadingTrackerResult.ChangeTrackingSet != null);

                    // Ideally, we reload prior state so that the caller can query changes and do incremental work.
                    // In this case, we already validated the correlating atomic save token, and so the state is safe to reuse.
                    tracker = ResumeTrackingChanges(
                        pm.LoggingContext,
                        loadingTrackerResult.FileId,
                        volumeMap,
                        journal,
                        loadingTrackerResult.ChangeTrackingSet,
                        buildEngineFingerprint);
                }
                else
                {
                    Contract.Assert(loadingTrackerResult.ChangeTrackingSet == null);

                    // Or, we might be unable to re-use the persisted state. In that case we start over. Note that there's nothing to do with the correlating save token here;
                    // on save, a new or existing token will be provided as appropriate.
                    // The reason of the failure is already logged in the TryLoad() method above.
                    tracker = StartTrackingChanges(pm.LoggingContext, volumeMap, journal, buildEngineFingerprint);
                }

                Logger.Log.LoadingChangeTracker(
                    pm.LoggingContext,
                    path,
                    loadingTrackerResult.FileId.ToString(),
                    loadingTrackerResult.Status.ToString(),
                    loadingTrackerResult.StatusAsString,
                    loadingTrackerResult.TrackedVolumesCount,
                    (long)loadingTrackerResult.TrackedJournalsSizeBytes,
                    loadingTrackerResult.DurationMs);

                return loadingTrackerResult;
            }
        }

        /// <summary>
        /// Loads <see cref="FileChangeTracker"/>, and if successful, the <see cref="FileChangeTracker"/> is a disabled one.
        /// </summary>
        public static LoadingTrackerResult LoadTrackingChanges(
            LoggingContext loggingContext,
            VolumeMap volumeMap,
            IChangeJournalAccessor journal,
            string path,
            string buildEngineFingerprint,
            out FileChangeTracker tracker,
            bool loadForAllCapableVolumes = true)
        {
            Contract.Requires(!loadForAllCapableVolumes || volumeMap != null);
            Contract.Requires(!loadForAllCapableVolumes || journal != null);
            Contract.Requires(path != null);

            tracker = null;

            using (var pm = BuildXL.Tracing.PerformanceMeasurement.StartWithoutStatistic(
                loggingContext,
                loggingContext1 => Logger.Log.StartLoadingChangeTracker(loggingContext1, path),
                loggingContext1 => Logger.Log.EndLoadingChangeTracker(loggingContext1)))
            {
                // Note that TryLoad may throw in the event of spooky I/O errors.
                var loadingTrackerResult = TryLoad(
                    pm.LoggingContext,
                    path,
                    volumeMap,
                    journal,
                    buildEngineFingerprint,
                    loadForAllCapableVolumes: loadForAllCapableVolumes);

                if (loadingTrackerResult.Succeeded)
                {
                    Contract.Assert(loadingTrackerResult.ChangeTrackingSet != null);

                    tracker = new FileChangeTracker(
                        pm.LoggingContext,
                        loadingTrackerResult.FileId,
                        FileChangeTrackingState.TrackingChanges,
                        loadingTrackerResult.ChangeTrackingSet.VolumeMap,
                        journal ?? new InProcChangeJournalAccessor(),
                        loadingTrackerResult.ChangeTrackingSet,
                        buildEngineFingerprint);
                }
                else
                {
                    Contract.Assert(loadingTrackerResult.ChangeTrackingSet == null);
                }

                Logger.Log.LoadingChangeTracker(
                    pm.LoggingContext,
                    path,
                    loadingTrackerResult.FileId.ToString(),
                    loadingTrackerResult.Status.ToString(),
                    loadingTrackerResult.StatusAsString,
                    loadingTrackerResult.TrackedVolumesCount,
                    (long)loadingTrackerResult.TrackedJournalsSizeBytes,
                    loadingTrackerResult.DurationMs);

                return loadingTrackerResult;
            }
        }

        /// <summary>
        /// Loads a persisted change tracking set. Note that change tracking sets are machine-specific
        /// and so should not be shared among machines. The load will fail if the provided atomic save token does
        /// not match the one stored in the persisted change tracking set; this allows referencing
        /// a change tracking set in some secondary location and only using it if it corresponds. See class remarks.
        /// </summary>
        /// <remarks>
        /// This operation fails gracefully (no exception) if the file or path does not exist, or if the correlating atomic save token is mismatched.
        /// </remarks>
        [SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public static LoadingTrackerResult TryLoad(
            LoggingContext loggingContext,
            string path,
            VolumeMap volumeMap,
            IChangeJournalAccessor journal,
            string buildEngineFingerprint,
            bool loadForAllCapableVolumes = true)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(!loadForAllCapableVolumes || volumeMap != null);
            Contract.Requires(!loadForAllCapableVolumes || journal != null);

            Stopwatch stopwatch = Stopwatch.StartNew();

            SafeFileHandle handle;
            OpenFileResult result = FileUtilities.TryCreateOrOpenFile(
                path,
                FileDesiredAccess.GenericRead,
                FileShare.Read | FileShare.Delete,
                FileMode.Open,
                // Ok to evict the file from standby since the file will be overwritten and never reread from disk after this point.
                FileFlagsAndAttributes.FileFlagSequentialScan,
                out handle);

            if (result.Succeeded)
            {
                Contract.Assume(handle != null);
                Contract.Assume(!handle.IsInvalid);

                using (handle)
                {
                    using (var stream = new FileStream(handle, FileAccess.Read))
                    {
                        FileEnvelopeId fileEnvelopeId;

                        try
                        {
                            fileEnvelopeId = FileEnvelope.ReadHeader(stream);
                        }
                        catch (BuildXLException e)
                        {
                            return LoadingTrackerResult.FailBadFormatMarker(e.Message);
                        }

                        try
                        {
                            return ExceptionUtilities.HandleRecoverableIOException(
                                () =>
                                {
                                    using (var reader = new BuildXLReader(debug: false, stream: stream, leaveOpen: true))
                                    {
                                        bool wasTrackerDisabled = reader.ReadBoolean();

                                        if (wasTrackerDisabled)
                                        {
                                            return LoadingTrackerResult.FailPriorTrackerDisabled();
                                        }
                                        
                                        var previousFingerprint = reader.ReadNullable(r => r.ReadString());
                                        // only check for fingerprints match if the supplied fingerprint is valid
                                        // this is to support special cases where we might want to load ChangeTracker
                                        // regardless of the previously stored fingerprint value
                                        if (buildEngineFingerprint != null && !string.Equals(previousFingerprint, buildEngineFingerprint, StringComparison.Ordinal))
                                        {
                                            return LoadingTrackerResult.FailBuildEngineFingerprintMismatch();
                                        }

                                        return FileChangeTrackingSet.TryLoad(
                                            loggingContext,
                                            fileEnvelopeId,
                                            reader,
                                            volumeMap,
                                            journal,
                                            stopwatch,
                                            loadForAllCapableVolumes);
                                    }
                                },
                                ex =>
                                {
                                    throw new BuildXLException(ex.Message);
                                });
                        }
                        catch (Exception e)
                        {
                            // Catch any exception. Failure in loading FileChangeTracker should not
                            // cause build break, or worse, make people stuck on erroneous state.
                            // In such a case, BuildXL simply has to start tracking from scratch.
                            return LoadingTrackerResult.FailLoadException(e.GetLogEventMessage());
                        }
                    }
                }
            }

            Contract.Assume(handle == null);
            return LoadingTrackerResult.FailTrackingSetCannotBeOpened(result.CreateFailureForError().DescribeIncludingInnerFailures());
        }

        /// <summary>
        /// Creates a new change tracker in the <see cref="FileChangeTrackingState.DisabledSinceTrackingIsIncomplete"/> state.
        /// Adding tracked files will be a no-op. This is suitable even given volumes that do not support change journaling, as required for tracking changes.
        /// </summary>
        public static FileChangeTracker CreateDisabledTracker(LoggingContext loggingContext)
        {
            Contract.Requires(loggingContext != null);
            Contract.Ensures(Contract.Result<FileChangeTracker>().TrackingState == FileChangeTrackingState.DisabledSinceTrackingIsIncomplete);

            return new FileChangeTracker(loggingContext);
        }

        #endregion Start, resume, or load tracker

        #region Save tracker

        /// <summary>
        /// Get the file envelope id to save with.  If no change has been made, reuse existing file envelope id.  Otherwise, use overrideFileEnvelopeId or a new id if overrideFileEnvelopeId is not specified. 
        /// </summary>
        public FileEnvelopeId GetFileEnvelopeToSaveWith(FileEnvelopeId? overrideFileEnvelopeId = default)
        {
            if (TrackingState == FileChangeTrackingState.TrackingChanges && !HasNewFileOrCheckpointData)
            {
                return FileEnvelopeId;
            }

            // Use override when provided, otherwise use the existing file id if we are building the initial change tracking set, 
            // otherwise recreate a new file id.
            return overrideFileEnvelopeId ?? (TrackingState == FileChangeTrackingState.BuildingInitialChangeTrackingSet ? FileEnvelopeId : FileEnvelopeId.Create());
        }

        /// <summary>
        /// Saves state to later resume change tracking where there is new file or checkpoint data.
        /// </summary>
        public bool SaveTrackingStateIfChanged(string path, FileEnvelopeId fileEnvelopeIdToSaveWith)
        {
            if (TrackingState == FileChangeTrackingState.TrackingChanges && !HasNewFileOrCheckpointData)
            {
                return false;
            }

            // Save tracking set if it is disabled or has new checkpoint data.
            Save(fileEnvelopeIdToSaveWith, path);

            return true;
        }

        /// <summary>
        /// Saves this change tracking set so that it can be reloaded. Note that change tracking sets are machine-specific
        /// and so should not be shared among machines. The provided <paramref name="atomicSaveToken"/> allows correlation of
        /// the persisted change tracking set to related incremental state.
        /// </summary>
        /// <exception cref="BuildXLException">Thrown in the event of an I/O error in saving tracking state.</exception>
        [SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        private void Save(FileEnvelopeId atomicSaveToken, string path)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(path));

            FileUtilities.DeleteFile(path);
            FileUtilities.CreateDirectory(Path.GetDirectoryName(path));

            using (var stream = FileUtilities.CreateFileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Delete,
                // Do not write the file with SequentialScan since it will be reread in the subsequent build
                FileOptions.None))
            {
                ExceptionUtilities.HandleRecoverableIOException(
                    () =>
                    {
                        using (var pm = BuildXL.Tracing.PerformanceMeasurement.StartWithoutStatistic(
                            m_loggingContext,
                            loggingContext => Logger.Log.StartSavingChangeTracker(loggingContext, path),
                            loggingContext => Logger.Log.EndSavingChangeTracker(loggingContext)))
                        {
                            Stopwatch sw = Stopwatch.StartNew();

                            FileEnvelope.WriteHeader(stream, atomicSaveToken);
                            using (var writer = new BuildXLWriter(debug: false, stream: stream, leaveOpen: true, logStats: false))
                            {
                                if (IsDisabledOrNullTrackingSet)
                                {
                                    writer.Write(true);
                                }
                                else
                                {
                                    writer.Write(false);
                                    writer.Write(m_buildEngineFingerprint, (w, s) => w.Write(s));
                                    m_changeTrackingSet.Save(writer);
                                }
                            }

                            FileEnvelope.FixUpHeader(stream, atomicSaveToken);

                            Logger.Log.SavingChangeTracker(
                                pm.LoggingContext,
                                path,
                                atomicSaveToken.ToString(),
                                m_changeTrackingSet == null ? "Null" : TrackingState.ToString(),
                                m_changeTrackingSet == null ? 0 : m_changeTrackingSet.TrackedVolumes.Count(),
                                sw.ElapsedMilliseconds);
                        }
                    },
                    ex => { throw new BuildXLException("Failed to save file change tracker", ex); });
            }
        }

        #endregion Save tracker

        #region Track path or directory

        /// <summary>
        /// Attempts to add the provided file to the change tracking set. <paramref name="maybeIdentity"/> (if provided)
        /// must correspond to the file identity of <paramref name="handle"/>.
        /// See <see cref="FileChangeTrackingSet.TryTrackChangesToFile"/>.
        /// On failure, the tracker will transition to the <see cref="FileChangeTrackingState.DisabledSinceTrackingIsIncomplete"/> state
        /// and <see cref="FileChangeTrackingSubscription.Invalid"/> is returned.
        /// </summary>
        /// <remarks>
        /// We could change the return type to <c>Possible</c> with a descriptive failure object when tracking fails, as is actually provided
        /// by <see cref="FileChangeTrackingSet"/>; instead, we take responsibility for logging here and choose to return a very lightweight failure
        /// (an invalid subscription). We wish to avoid extra overhead when tracking is intentionally disabled (see <see cref="CreateDisabledTracker"/>).
        /// We allow providing an identity to avoid querying identity twice (files are often also added to a <see cref="FileContentTable"/>,
        /// which also needs an identity).
        /// Note that the provided <paramref name="maybeIdentity"/> is allowed to be of any identity kind, even <see cref="VersionedFileIdentity.IdentityKind.Anonymous"/>
        /// (which may be used to reference the absence of an available identity); only some kinds are actually suitable for this change tracker.
        /// Providing an unusable kind fails gracefully (latches to disabled), just as if an identity was not provided at all (and identity query failed).
        /// </remarks>
        public FileChangeTrackingSubscription TryTrackChangesToFile(
            SafeFileHandle handle,
            string path,
            VersionedFileIdentity? maybeIdentity = null,
            TrackingUpdateMode updateMode = TrackingUpdateMode.Preserve)
        {
            Contract.Requires(handle != null);
            Contract.Requires(path != null);

            if (IsDisabledOrNullTrackingSet)
            {
                return FileChangeTrackingSubscription.Invalid;
            }

            Possible<FileChangeTrackingSubscription> possibleSubscription =
                m_changeTrackingSet.TryTrackChangesToFile(handle, path, maybeIdentity, updateMode);

            if (!possibleSubscription.Succeeded)
            {
                DisableTracking(path, possibleSubscription.Failure);
                return FileChangeTrackingSubscription.Invalid;
            }

            return possibleSubscription.Result;
        }

        /// <summary>
        /// Combined operation of opening and tracking a directory (or its absence), enumerating it, and then tracking changes to that enumeration result (its membership).
        /// The membership of the directory will be invalidated if a name is added or removed directly inside the directory (i.e., when <c>FindFirstFile</c>
        /// and <c>FindNextFile</c> would see a different set of names).
        /// </summary>
        public Possible<FileChangeTrackingSet.EnumerationResult> TryEnumerateDirectoryAndTrackMembership(string path, Action<string, FileAttributes> handleEntry)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(path));
            Contract.Requires(handleEntry != null);

            if (IsDisabledOrNullTrackingSet)
            {
                var possibleFingerprintResult = DirectoryMembershipTrackingFingerprinter.ComputeFingerprint(path, handleEntry);
                if (!possibleFingerprintResult.Succeeded)
                {
                    return possibleFingerprintResult.Failure;
                }

                return new FileChangeTrackingSet.EnumerationResult(
                    possibleFingerprintResult.Result.Fingerprint, 
                    possibleFingerprintResult.Result.PathExistence, 
                    new Failure<string>("Tracking set is disabled"));
            }

            // Note that we still attempt to enumerate-and-track even if this tracker is already disabled (but was created with a tracking set). We need the result if possible
            // (and if additional tracking succeeds while disabled, there's no harm).
            Possible<FileChangeTrackingSet.EnumerationResult> possibleEnumerationResult = m_changeTrackingSet.TryEnumerateDirectoryAndTrackMembership(
                path,
                handleEntry);

            if (!possibleEnumerationResult.Succeeded)
            {
                DisableTracking(path, possibleEnumerationResult.Failure);
                return possibleEnumerationResult.Failure;
            }

            return possibleEnumerationResult;
        }

        /// <summary>
        /// Tries to track directory membership given the directory's members.
        /// </summary>
        public Possible<FileChangeTrackingSet.EnumerationResult> TryTrackDirectoryMembership(
            string path,
            IReadOnlyList<(string, FileAttributes)> members)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(path));

            if (IsDisabledOrNullTrackingSet)
            {
                var fingerprint = DirectoryMembershipTrackingFingerprinter.ComputeFingerprint(members);
                return new FileChangeTrackingSet.EnumerationResult(
                    fingerprint,
                    PathExistence.ExistsAsDirectory,
                    new Failure<string>("Tracking set is disabled"));
            }

            Possible<FileChangeTrackingSet.EnumerationResult> possibleEnumerationResult = m_changeTrackingSet.TryTrackDirectoryMembership(path, members);

            if (!possibleEnumerationResult.Succeeded)
            {
                DisableTracking(path, possibleEnumerationResult.Failure);
                return possibleEnumerationResult.Failure;
            }

            return possibleEnumerationResult;
        }

        /// <summary>
        /// Tracks a non-existent relative path chain from a tracked parent root.
        /// If trackedParentPath = 'C:\foo' and relativeAbsentPath = 'a\b\c'
        /// Then the following paths are tracked as absent: 'C:\foo\a', 'C:\foo\a\b', 'C:\foo\a\b\c'.
        /// ADVANCED. Use with care. This should only because if the relative has been guaranteed to be non-existent
        /// because the parent path non-existent or enumerated and the child path was non-existent
        /// </summary>
        public bool TrackAbsentRelativePath([NotNull] string trackedParentPath, [NotNull] string relativeAbsentPath)
        {
            return m_changeTrackingSet?.TrackAbsentRelativePath(trackedParentPath, relativeAbsentPath) ?? false;
        }

        /// <summary>
        /// Probes for the existence of a path, while possibly also tracking the result.
        /// </summary>
        /// <returns>A <see cref="PathExistence"/>.</returns>
        /// <remarks>
        /// If the change-tracking set is unset (or null), then this method only probes for the exisitence of a path.
        /// If probing succeeds, but tracking fails (in case the change-tracking set is not null), then a <see cref="PathExistence"/> is still returned,
        /// but tracking is then marked incomplete via a transition to the <see cref="FileChangeTrackingState.DisabledSinceTrackingIsIncomplete"/> state.
        /// If a file does not exist and then is later created, that change will be detected.
        /// </remarks>
        public Possible<PathExistence> TryProbeAndTrackPath(string path, bool? isReadOnly = default)
        {
            Contract.Requires(path != null);

            // There was a bug related to different behavior when incremental scheduling is on/off.
            // Read the bug here: Bug #884766
            if (m_changeTrackingSet == null)
            {
                return FileUtilities.TryProbePathExistence(path, followSymlink: false).WithGenericFailure();
            }

            // Note that we still attempt to probe-and-track even if this tracker is already disabled (but was created with a tracking set). We need the TrackedExistence result if possible
            // (and if tracking of that additional path succeeds while disabled, there's no harm).
            Possible<FileChangeTrackingSet.ProbeResult> possibleProbeResult = m_changeTrackingSet.TryProbeAndTrackPath(path, isReadOnly: isReadOnly);

            return possibleProbeResult.Then(
                probeResult =>
                {
                    if (!probeResult.PossibleTrackingResult.Succeeded)
                    {
                        DisableTracking(path, probeResult.PossibleTrackingResult.Failure);
                    }

                    return probeResult.Existence;
                });
        }

        private void DisableTracking(string path, Failure trackingFailure)
        {
            int disabledValue = (int)FileChangeTrackingState.DisabledSinceTrackingIsIncomplete;

            if (!m_volumeMap.SkipTrackingJournalIncapableVolume)
            {
                // Immediately disable tracking.
                DoDisable(path, trackingFailure);
            }
            else
            {
                // Check if the path is in the volume that can be skipped.
                // This is expensive, and should only be used during testing.

                if (Interlocked.CompareExchange(ref m_trackingStateValue, disabledValue, disabledValue) == disabledValue)
                {
                    return;
                }

                OpenFileResult openResult = FileUtilities.TryOpenDirectory(
                        Path.GetPathRoot(path),
                        FileDesiredAccess.GenericRead,
                        FileShare.ReadWrite | FileShare.Delete,
                        FileFlagsAndAttributes.FileFlagOverlapped | FileFlagsAndAttributes.FileFlagOpenReparsePoint,
                        out SafeFileHandle handle);

                if (openResult.Succeeded)
                {
                    using (handle)
                    {
                        ulong volumeSerial = FileUtilities.GetVolumeSerialNumberByHandle(handle);
                        if (m_changeTrackingSet.IsTrackedVolume(volumeSerial))
                        {
                            DoDisable(path, trackingFailure);
                        }
                    }
                }
                else
                {
                    DoDisable(path, openResult.CreateFailureForError());
                }                    
            }

            void DoDisable(string p, Failure f)
            {
                if (Interlocked.Exchange(ref m_trackingStateValue, disabledValue) != disabledValue)
                {
                    Logger.Log.DisableChangeTracker(m_loggingContext, p, f.DescribeIncludingInnerFailures());
                }
            }
        }

        /// <summary>
        /// Clear the set of tracked directory membership fingerprints.
        /// </summary>
        /// <remarks>
        /// For DScript, if graph cache is missed, we need to create a new set of directory fingerprints.
        /// </remarks>
        public void ClearDirectoryFingerprints()
        {
            if (IsDisabledOrNullTrackingSet)
            {
                return;
            }

            m_changeTrackingSet.ClearDirectoryFingerprints();
        }

        #endregion Track path or directory

        #region Scan change journal

        /// <summary>
        /// Processes changes to files since the last checkpoint.
        /// If processing is not aborted, the volume checkpoints for the change tracking set are updated
        /// to avoid re-processing the same changes subsequently. The return value indicates if processing
        /// was successful (not aborted). If an error occurs during processing, a new change tracking set will be created
        /// in the <see cref="FileChangeTrackingState.BuildingInitialChangeTrackingSet"/> state.
        /// Note that following a checkpoint update, the caller should re-persist
        /// the tracking set (if applicable) so that the checkpointing is effective on reload.
        /// Note that this method should not be called if this tracker is in the <see cref="FileChangeTrackingState.BuildingInitialChangeTrackingSet"/> state
        /// or <see cref="FileChangeTrackingState.DisabledSinceTrackingIsIncomplete"/> state, since those indicate that
        /// a complete and accurate change tracking set is not available.
        /// </summary>
        /// <remarks>
        /// Processing changes should not be concurrent with tracking files (<see cref="TryTrackChangesToFile"/>).
        /// </remarks>
        public ScanningJournalResult TryProcessChanges(TimeSpan? timeLimit)
        {
            // The caller should check if the tracker is in the correct state before calling this method to process changes.
            Contract.Requires(TrackingState == FileChangeTrackingState.TrackingChanges);

            using (var pm = BuildXL.Tracing.PerformanceMeasurement.StartWithoutStatistic(
                m_loggingContext,
                loggingContext => Logger.Log.StartScanningJournal(loggingContext),
                loggingContext => Logger.Log.EndScanningJournal(m_loggingContext)))
            {
                var scanningJournalResult = m_changeTrackingSet.TryProcessChanges(m_journal, timeLimit);

                if (!scanningJournalResult.Succeeded)
                {
                    m_trackingStateValue = (int)FileChangeTrackingState.BuildingInitialChangeTrackingSet;
                    m_changeTrackingSet = FileChangeTrackingSet.CreateForAllCapableVolumes(m_loggingContext, m_volumeMap, m_journal);
                }

                ReportProcessChangesCompletion(scanningJournalResult);

                Logger.Log.ScanningJournal(
                    m_loggingContext,
                    FileEnvelopeId.ToString(),
                    scanningJournalResult.Status.ToString(),
                    scanningJournalResult.TotalDuration.ToMilliseconds());

                return scanningJournalResult;
            }
        }

        #endregion Scan change journal

        #region Text writer

        /// <summary>
        /// Writes textual format of <see cref="FileChangeTracker"/>.
        /// </summary>
        public void WriteText(TextWriter writer)
        {
            Contract.Requires(writer != null);

            writer.WriteLine(">> ============================ File Change Tracker ============================ <<");
            m_changeTrackingSet?.WriteText(writer);
        }

        #endregion Text writer

        #region Observable

        /// <inheritdoc />
        public IDisposable Subscribe(IObserver<ChangedPathInfo> observer)
        {
            Contract.Requires(observer != null);

            return m_changeTrackingSet?.Subscribe(observer);
        }

        /// <inheritdoc />
        public IDisposable Subscribe(IObserver<ChangedFileIdInfo> observer)
        {
            Contract.Requires(observer != null);

            return m_changeTrackingSet?.Subscribe(observer);
        }

        /// <inheritdoc />
        public IDisposable Subscribe(IFileChangeTrackingObserver observer)
        {
            Contract.Requires(observer != null);

            if (!m_observers.Contains(observer))
            {
                m_observers.Add(observer);
            }

            return new FileChangeTrackerUnsubscriber(
                m_observers,
                observer,
                new List<IDisposable> { Subscribe((IObserver<ChangedFileIdInfo>) observer), Subscribe((IObserver<ChangedPathInfo>) observer) });
        }

        private void ReportProcessChangesCompletion(ScanningJournalResult result)
        {
            foreach (var observer in m_observers)
            {
                observer.OnCompleted(result);
            }
        }

        #endregion Observable
    }
}
