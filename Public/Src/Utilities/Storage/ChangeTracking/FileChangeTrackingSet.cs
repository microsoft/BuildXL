// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using BuildXL.Storage.ChangeJournalService;
using BuildXL.Storage.ChangeJournalService.Protocol;
using BuildXL.Storage.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Represents a set of files with version information for which changes should be tracked.
    /// This set multiplexes zero or more per-volume sets.
    /// </summary>
    /// <remarks>
    /// A change-tracking set stores sufficient information to emit path-based invalidations from change journal records
    /// (which do not contain full path information). Though the minimal set of change notifications is not guaranteed
    /// (this is an accuracy / space tradeoff), a 'sufficient' set of notifications is guaranteed since volume change journals are assumed loss-less.
    /// Here, 'sufficient' means that the notifications should not miss any differences that would be detected by comparing full 'before'
    /// and 'after' snapshots of the entire volume - i.e., we detect even renames of parent directories (invalidates entire subtrees).
    ///
    /// Change tracking follows a 'manual-reset' model:
    /// - A file of interest at path P is tracked. This hands back a 'subscription'.
    /// - A change eventually happens (data change, parent directory rename, etc.)
    /// - Changes are scanned for; a change notification for path P is emitted.
    /// *P is no longer tracked*
    ///
    /// Consumers are expected to re-track changed files if they still happen to be relevant.
    ///
    /// This means that a change-tracking set is in fact a set of tracked paths (including parent directories), and each change-notification removes
    /// a path from the set. For generating 'sufficient' notifications, we can reason very simply on the notion that the 'track' operation
    /// has as a post-condition that enough state to guarantee eventual invalidation *that single path and all parents* has been stored.
    /// Succinctly, we call a path 'tracked' once this state is stored.
    ///
    /// ==Implementation details==
    /// This implementation discovers parent directories as-needed. This is only possible due to a delightful filesystem quirk:
    ///
    ///     A directory D may not be renamed if there exists any open handle for a path under D.
    ///
    /// This means that we can take a snapshot of each path component in A\B\C\F so long as a handle to F is held open.
    /// Since the handle is only released after all path components are recorded, we are not susceptible to parent-directory renames
    /// while recording state.
    ///
    /// As an accuracy / space tradeoff, we only note a path as 'tracked' or 'untracked' and do not add any additional state for
    /// an already-tracked path. Consider the following sequence:
    /// - CreateSourceFile D\A
    /// - Track A
    /// - Rename D -> D2
    /// - CreateSourceFile D\B
    /// - Track D\B
    ///
    /// A minimal set of change notifications would be simply 'D\A', i.e., the 'track' call for D\A is invalidated.
    /// However, since this is an eccentric case, we choose to not store the various incarnations of the path D, and instead
    /// just that D (any version) is tracked (above, we would emit invalidations for both D\A and D\B).
    /// This means that a directory-handle to D is opened only once (the first time), and the second version of D (i.e., its File ID and USN)
    /// is never observed. Instead, we note that the original D was fully tracked by the time we released the handle to D\A, and so
    /// we will see its rename event; so long as we invalidate entire subtrees on rename events, the identity of D\B's actual parent can't be relevant.
    ///
    /// From a data-structure standpoint, we need to store a few distinct things:
    /// - A hierarchy of currently-tracked paths, which we can enumerate top-down (invalidate a subtree).
    /// - For each tracked path, a tuple (FileID, USN, Path) which is sufficient to invalidate the tracked path (or its subtree).
    ///
    /// For the first point, we use a private <see cref="PathTable"/> and use the per-node flags (i.e., <see cref="HierarchicalNameTable.SetFlags"/>)
    /// to record tracking state. This is fairly space efficient initially, but we are not presently able to evict unused paths (*open issue*).
    ///
    /// The internal path table is a private detail, and it may not be shared with any other since full control of the node flags is required.
    /// This is problematic for a few reasons:
    /// - We must then map between the internal path table and that of callers (currently via string expansion; *open issue*)
    /// - There is massive or entire overlap with the external tables, so this is a relevant disk and memory footprint cost (*open issue*)
    /// </remarks>
    public sealed class FileChangeTrackingSet : IObservable<ChangedPathInfo>, IObservable<ChangedFileIdInfo>
    {
        private const int MaxMessagePerChangeValidationType = 10;

        /// <summary>
        /// The flags to open handles in FileChangeTrackingSet
        /// </summary>
        /// <remarks>
        /// FileFlagOverlapped is required to open the handles for directories.
        /// FileFlagOpenReparsePoint allows us to open the source of the junctions if it is a junction.
        /// </remarks>
        private const FileFlagsAndAttributes OpenFlags = FileFlagsAndAttributes.FileFlagOverlapped | FileFlagsAndAttributes.FileFlagOpenReparsePoint;
        
        // We assign some tracking-related meanings to some of the available name-table flags.

        /// <summary>
        /// The path is tracked. We have a tracking record such that invalidations will occur if it is written, renamed, or deleted.
        /// </summary>
        private const HierarchicalNameTable.NameFlags Tracked = HierarchicalNameTable.NameFlags.Marked;

        /// <summary>
        /// The path was established to not exist. Some parent directory of the path is <see cref="Tracked"/>, and additions of children
        /// to that parent will cause absence of this path to be re-evaluated.
        /// </summary>
        /// <remarks>
        /// We re-probe for absent files when possibly invalidated since we don't have file name info from the journal. Otherwise, false-positives would be
        /// very high - consider a directory D\ in which temporary files are sometimes created (text editors for example often do this)
        /// but some single name D\NotHere is marked absent.
        /// </remarks>
        private const HierarchicalNameTable.NameFlags Absent = HierarchicalNameTable.NameFlags.Sealed;

        /// <summary>
        /// The path (presumably a directory at some point) was enumerated. Additions and deletions of names in the directory
        /// will cause the directory's membership fingerprint to be re-evaluated.
        /// </summary>
        /// <remarks>
        /// We calculate fingerprints for directory membership (rather than the caller) since we don't have file name info from the journal.
        /// Otherwise, false-positives can be very high - consider transient temporary files in some enumerated directory (never actually observed by a caller,
        /// but otherwise sufficient to potentially invalidate directory membership).
        /// </remarks>
        private const HierarchicalNameTable.NameFlags Enumerated = HierarchicalNameTable.NameFlags.Root;

        /// <summary>
        /// The path (presumably a directory at some point) has had child paths tracked.
        /// </summary>
        /// <remarks>
        /// We allow 'supersede' semantics on tracking, but only on files (to avoid orphaning tracked children).
        /// </remarks>
        private const HierarchicalNameTable.NameFlags Container = HierarchicalNameTable.NameFlags.Container;

        private readonly Dictionary<ulong, SingleVolumeFileChangeTrackingSet> m_perVolumeChangeTrackingSets;

        private readonly PathTable m_internalPathTable;

        private readonly ConcurrentDictionary<AbsolutePath, DirectoryMembershipTrackingFingerprint> m_directoryMembershipTrackingFingerprints;

        private bool m_hasNewFileOrCheckpointData;

        private readonly LoggingContext m_loggingContext;

        private readonly List<IObserver<ChangedPathInfo>> m_observers = new List<IObserver<ChangedPathInfo>>(2);

        /// <nodoc />
        public readonly CounterCollection<FileChangeTrackingCounter> Counters = new CounterCollection<FileChangeTrackingCounter>();

        /// <summary>
        /// Volume map used by this instance of <see cref="FileChangeTrackingSet"/>.
        /// </summary>
        public readonly VolumeMap VolumeMap;

        private int m_untrackedParentPathOnTrackingAbsentRelativePathCount = 0;

        /// <summary>
        /// Max concurrency threshold for processing changed records.
        /// </summary>
        /// <remarks>
        /// The rationale for this threshold is the more cores, the lower the threshold.
        /// </remarks>
        private static readonly int s_maxConcurrencyForRecordProcessing = Math.Max(16, 256 / Environment.ProcessorCount);

        private FileChangeTrackingSet(
            LoggingContext loggingContext,
            PathTable internalPathTable,
            VolumeMap volumeMap,
            Dictionary<ulong, SingleVolumeFileChangeTrackingSet> perVolumeChangeTrackingSets,
            ConcurrentDictionary<AbsolutePath, DirectoryMembershipTrackingFingerprint> membershipFingerprints,
            bool hasNewFileOrCheckpointData)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(volumeMap != null);
            Contract.Requires(perVolumeChangeTrackingSets != null);
            Contract.Requires(internalPathTable != null);
            Contract.Requires(membershipFingerprints != null);

            m_loggingContext = loggingContext;
            m_internalPathTable = internalPathTable;
            VolumeMap = volumeMap;
            m_perVolumeChangeTrackingSets = perVolumeChangeTrackingSets;
            m_hasNewFileOrCheckpointData = hasNewFileOrCheckpointData;
            m_directoryMembershipTrackingFingerprints = membershipFingerprints;

            foreach (SingleVolumeFileChangeTrackingSet trackingSet in m_perVolumeChangeTrackingSets.Values)
            {
                trackingSet.OwningFileChangeTrackingSet = this;
            }
        }

        /// <summary>
        /// Indicates if this change tracking set has an updated set of tracked files (vs. creation or load time)
        /// or if it has advanced the checkpoints of one or more volume journals. In either case, the change tracking
        /// set should be re-persisted, if applicable, to avoid repeated work.
        /// </summary>
        public bool HasNewFileOrCheckpointData => Volatile.Read(ref m_hasNewFileOrCheckpointData);

        /// <summary>
        /// Tracked volumes.
        /// </summary>
        public IEnumerable<ulong> TrackedVolumes => m_perVolumeChangeTrackingSets.Keys;

        /// <summary>
        /// Checks if a volume is being tracked.
        /// </summary>
        public bool IsTrackedVolume(ulong volumeSerial) => m_perVolumeChangeTrackingSets.ContainsKey(volumeSerial);

        /// <summary>
        /// Creates an empty change tracking set with an initial checkpoint at the current state of each capable volume.
        /// A volume is capable if it is successfully opened (media present etc.), its filesystem supports change journaling,
        /// and the volume change journal is enabled. Attempts to track files on incapable volumes will fail.
        /// </summary>
        public static FileChangeTrackingSet CreateForAllCapableVolumes(LoggingContext loggingContext, VolumeMap volumeMap, IChangeJournalAccessor journalAccessor)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(volumeMap != null);
            Contract.Requires(journalAccessor != null);

            ulong trackedJournalsSizeBytes;

            // Note that when creating an empty set, we set hasNewFileOrCheckpointData: true; this means that an empty set will get saved to disk,
            // which allows it to replace a prior, non-empty set for example. Otherwise, we would lack a fix-point for any usage in which the current set
            // on disk is unusable, but the newly created set never has files added (consider incremental scheduling without an execution phase).
            return CreateForAllCapableVolumesFromPriorCheckpoint(
                loggingContext,
                volumeMap,
                journalAccessor,
                internalPathTable: new PathTable(disableDebugTag: true), // TODO:409239: Fix debug tag allocation; don't disable here.
                membershipFingerprints: new ConcurrentDictionary<AbsolutePath, DirectoryMembershipTrackingFingerprint>(),
                knownCheckpoints: null,
                hasNewFileOrCheckpointData: true,
                trackedJournalsSizeBytes: out trackedJournalsSizeBytes,
                nextUsns: out Dictionary<ulong, Usn> dummyNextUsns);
        }

        /// <summary>
        /// Creates an empty change tracking set, but restoring the specified known checkpoints, internal path table, and other state.
        /// This can be used as a deserialization constructor (respecting existing checkpoints, session, and path IDs). Since this
        /// new change tracking set reflects the *current* volume map and volume capabilities, the caller should validate
        /// if needed that the new set of tracked volumes is compatible with what was serialized.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "QueryJournal")]
        private static FileChangeTrackingSet CreateForAllCapableVolumesFromPriorCheckpoint(
            LoggingContext loggingContext,
            VolumeMap volumeMap,
            IChangeJournalAccessor journalAccessor,
            bool hasNewFileOrCheckpointData,
            PathTable internalPathTable,
            ConcurrentDictionary<AbsolutePath, DirectoryMembershipTrackingFingerprint> membershipFingerprints,
            Dictionary<ulong, Usn> knownCheckpoints,
            out ulong trackedJournalsSizeBytes,
            out Dictionary<ulong, Usn> nextUsns)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(internalPathTable != null);
            Contract.Requires(membershipFingerprints != null);

            trackedJournalsSizeBytes = 0;
            var perVolumeChangeTrackingSets = new Dictionary<ulong, SingleVolumeFileChangeTrackingSet>();
            nextUsns = new Dictionary<ulong, Usn>();

            foreach (var volume in volumeMap.Volumes)
            {
                var query = new QueryJournalRequest(volume.Value);
                MaybeResponse<QueryUsnJournalResult> response = journalAccessor.QueryJournal(query);
                if (response.IsError)
                {
                    Logger.Log.ChangeDetectionFailCreateTrackingSetDueToJournalQueryError(
                        loggingContext,
                        unchecked((long)volume.Key),
                        volume.Value.ToString(),
                        response.Error.Status.ToString(),
                        response.Error.Message);
                    continue;
                }

                QueryUsnJournalResult journalStatus = response.Response;
                Usn? maybeNextUsn = default(Usn?);
                Usn? maybeCheckpoint = default(Usn?);

                if (journalStatus.Status == QueryUsnJournalStatus.Success)
                {
                    Contract.Assert(journalStatus.Succeeded);
                    Usn initialCheckpoint;

                    if (knownCheckpoints == null || !knownCheckpoints.TryGetValue(volume.Key, out initialCheckpoint))
                    {
                        // If there is no known checkpoint, get the latest USN in the journal.
                        initialCheckpoint = journalStatus.Data.NextUsn;
                    }

                    trackedJournalsSizeBytes += journalStatus.Data.MaximumSize;

                    perVolumeChangeTrackingSets.Add(
                        volume.Key,
                        new SingleVolumeFileChangeTrackingSet(
                            loggingContext,
                            internalPathTable,
                            volume.Value,
                            volume.Key,
                            journalId: journalStatus.Data.UsnJournalId,
                            initialCheckpoint: initialCheckpoint,
                            journalSizeInBytes: journalStatus.Data.MaximumSize));

                    nextUsns.Add(volume.Key, journalStatus.Data.NextUsn);

                    maybeCheckpoint = initialCheckpoint;
                    maybeNextUsn = journalStatus.Data.NextUsn;
                }

                Logger.Log.ChangeDetectionCreateResult(
                    loggingContext,
                    unchecked((long)volume.Key),
                    volume.Value.ToString(),
                    journalStatus.Status.ToString(),
                    maybeNextUsn.HasValue ? maybeNextUsn.Value.ToString() : string.Empty,
                    maybeCheckpoint.HasValue ? maybeCheckpoint.Value.ToString() : string.Empty);
            }

            return new FileChangeTrackingSet(
                loggingContext: loggingContext,
                internalPathTable: internalPathTable,
                volumeMap: volumeMap,
                perVolumeChangeTrackingSets: perVolumeChangeTrackingSets,
                membershipFingerprints: membershipFingerprints,
                hasNewFileOrCheckpointData: hasNewFileOrCheckpointData);
        }

        private static FileChangeTrackingSet CreateForKnownVolumesFromPriorCheckpoint(
            LoggingContext loggingContext,
            VolumeMap volumeMap,
            bool hasNewFileOrCheckpointData,
            PathTable internalPathTable,
            ConcurrentDictionary<AbsolutePath, DirectoryMembershipTrackingFingerprint> membershipFingerprints,
            Dictionary<ulong, ulong> knownJournalIds,
            Dictionary<ulong, Usn> knownCheckpoints)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(internalPathTable != null);
            Contract.Requires(membershipFingerprints != null);

            var perVolumeChangeTrackingSets = new Dictionary<ulong, SingleVolumeFileChangeTrackingSet>();

            foreach (var volume in volumeMap.Volumes)
            {
                ulong journalId;
                Usn initialCheckpoint;

                if (knownJournalIds == null || !knownJournalIds.TryGetValue(volume.Key, out journalId))
                {
                    continue;
                }

                if (knownCheckpoints == null || !knownCheckpoints.TryGetValue(volume.Key, out initialCheckpoint))
                {
                    continue;
                }

                perVolumeChangeTrackingSets.Add(
                    volume.Key,
                    new SingleVolumeFileChangeTrackingSet(
                        loggingContext,
                        internalPathTable,
                        volume.Value,
                        volume.Key,
                        journalId: journalId,
                        initialCheckpoint: initialCheckpoint,
                        journalSizeInBytes: 0));

                Logger.Log.ChangeDetectionCreateResult(
                    loggingContext,
                    unchecked((long)volume.Key),
                    volume.Value.ToString(),
                    QueryUsnJournalStatus.Success.ToString(),
                    string.Empty, // Journal is not queried.
                    initialCheckpoint.ToString());
            }

            return new FileChangeTrackingSet(
                loggingContext: loggingContext,
                internalPathTable: internalPathTable,
                volumeMap: volumeMap,
                perVolumeChangeTrackingSets: perVolumeChangeTrackingSets,
                membershipFingerprints: membershipFingerprints,
                hasNewFileOrCheckpointData: hasNewFileOrCheckpointData);
        }

        /// <summary>
        /// Clear the set of tracked directory membership fingerprints.
        /// </summary>
        /// <remarks>
        /// For DScript, if graph cache is missed, we need to create a new set of directory fingerprints.
        /// </remarks>
        public void ClearDirectoryFingerprints()
        {
            m_directoryMembershipTrackingFingerprints.Clear();
        }

        /// <summary>
        /// Attempts to read the change journal for each tracked volume their current checkpoints.
        /// </summary>
        public ScanningJournalResult TryProcessChanges(IChangeJournalAccessor journalAccessor, TimeSpan? timeLimitForJournalScanning)
        {
            Contract.Ensures(Contract.Result<ScanningJournalResult>().Status != ScanningJournalStatus.NotChecked);

            var result = ScanningJournalResult.NotChecked;
            foreach (var volumeSerial in TrackedVolumes)
            {
                result += TryProcessChanges(volumeSerial, journalAccessor, timeLimitForJournalScanning);

                if (!result.Succeeded)
                {
                    // If scanning journal for one volume failed, do not continue.
                    return result;
                }
            }

            return result;
        }

        /// <summary>
        /// Attempts to read the specified volume's change journal from the current checkpoint.
        /// </summary>
        public ScanningJournalResult TryProcessChanges(
            ulong volumeSerial,
            IChangeJournalAccessor journalAccessor,
            TimeSpan? timeLimitForJournalScanning)
        {
            const int ReportedChangedPathsSize = 10;
            var reportedChangedPaths = new List<ChangedPathInfo>(ReportedChangedPathsSize);

            return TryProcessChanges(
                volumeSerial,
                journalAccessor,
                timeLimitForJournalScanning,
                (changes, path) =>
                {
                    var changedPathInfo = new ChangedPathInfo(path, changes);
                    ReportChangedPath(changedPathInfo);

                    if (reportedChangedPaths.Count < ReportedChangedPathsSize)
                    {
                        reportedChangedPaths.Add(changedPathInfo);
                    }
                },
                ReportChangedPathError,
                () =>
                {
                    ReportChangedPathCompletion();
                    LogReportedChangedPath(volumeSerial, reportedChangedPaths);
                });
        }

        private void LogReportedChangedPath(ulong volumeSerial, List<ChangedPathInfo> reportedChangedPaths)
        {
            if (reportedChangedPaths.Count == 0)
            {
                return;
            }

            var volumeGuidPath = m_perVolumeChangeTrackingSets[volumeSerial].VolumeGuidPath;
            string changes = string.Join(Environment.NewLine, reportedChangedPaths.Select(c => I($"\t{c.Path}: {c.PathChanges}")));
            Logger.Log.ChangedPathsDetectedByJournalScanning(m_loggingContext, unchecked((long)volumeSerial), volumeGuidPath.ToString(), changes);
        }

        /// <summary>
        /// Attempts to read the specified volume's change journal from the current checkpoint.
        /// Relevant records for tracked paths are processed with <paramref name="handleChangedPath"/>
        /// as they are encountered.
        /// </summary>
        private ScanningJournalResult TryProcessChanges(
            ulong volumeSerial,
            IChangeJournalAccessor journalAccessor,
            TimeSpan? timeLimitForJournalScanning,
            Action<PathChanges, string> handleChangedPath,
            Action<ScanningJournalException> handleChangedPathError = null,
            Action handleChangedPathCompletion = null)
        {
            // As part of invalidating 'Absent' flags, we collect those paths marked Absent that now may or may not exist according to this scan.
            // We defer processing of these paths since we wish to re-track them if still absent; the tracking set may not be updated while processing changes.
            var possiblyNewlyPresentPaths = new HashSet<AbsolutePath>();

            // Similarly, as part of invalidating 'Enumerated' flags, we can sometimes double-check that a fingerprint of membership is actually unchanged
            // (this hides creation and then deletion of temp files, for example).
            var possibleMembershipChanges = new Dictionary<AbsolutePath, DirectoryMembershipTrackingFingerprint>();

            // Possibly hard-link changes.
            var possiblyHardLinkChanges = new List<(AbsolutePath path, UsnRecord usnRecord)>();
            var handledChanges = new HashSet<FileId>();

            IReadOnlyList<AbsolutePath> unchangedJunctionRoots = VolumeMap.UnchangedJunctionRoots.Select(a => AbsolutePath.Create(m_internalPathTable, a)).ToList();

            SingleVolumeFileChangeTrackingSet volumeTrackingSet = m_perVolumeChangeTrackingSets[volumeSerial];

            CounterCollection<ReadJournalCounter> stats = new CounterCollection<ReadJournalCounter>();
            MaybeResponse<ReadJournalResponse> readResponse;

            using (stats.StartStopwatch(ReadJournalCounter.ReadRelevantJournalDuration))
            {
                readResponse = volumeTrackingSet.ReadRelevantChangesSinceCheckpoint(
                    journalAccessor,
                    unchangedJunctionRoots,
                    timeLimitForJournalScanning,
                    stats,
                    (changeType, internalPath, usnRecord) =>
                    {
                        string expandedPath = internalPath.ToString(m_internalPathTable);

                        if (unchangedJunctionRoots.Contains(internalPath))
                        {
                            // The junction has been deleted and recreated; but it points to the same target.
                            // BuildXL did not invalidate the child paths under this junction; but it invalidated the junction itself.
                            // That's why, BuildXL needs to retrack the junction with its new source file id. 
                            Analysis.IgnoreResult(TryProbeAndTrackPathInternal(internalPath));
                            return;
                        }

                        // When an Enumeration dependency is invalidated, we clean up the associate membership fingerprint.
                        // The special Conflict fingerprint is just like not having a fingerprint at all - we can't double-check
                        // the current fingerprint because there were multiple distinct fingerprints recorded.
                        DirectoryMembershipTrackingFingerprint? maybeMembershipFingerprint = null;

                        if ((changeType & PathChanges.MembershipChanged) != 0)
                        {
                            DirectoryMembershipTrackingFingerprint membershipFingerprint;
                            if (m_directoryMembershipTrackingFingerprints.TryRemove(internalPath, out membershipFingerprint) &&
                                membershipFingerprint != DirectoryMembershipTrackingFingerprint.Conflict)
                            {
                                maybeMembershipFingerprint = membershipFingerprint;
                            }
                        }

                        if (changeType.IsNewlyPresent())
                        {
                            // If the _only_ change reason is NewlyPresent, then we will double-check that the path is actually absent.
                            // False-positives can be fairly high since some addition D\F can invalidate the absent flag on a sibling D\G
                            // (since we don't see filenames in the journal records).
                            possiblyNewlyPresentPaths.Add(internalPath);
                        }
                        else if (changeType == PathChanges.MembershipChanged && maybeMembershipFingerprint.HasValue)
                        {
                            // Similarly for enumeration. Note we tried to find a fingerprint above, but maybe it was Conflict.
                            // We will emit membership changes below where we call 'CheckAndMaybeInvalidateEnumerationDependencies'
                            possibleMembershipChanges.Add(internalPath, maybeMembershipFingerprint.Value);
                        }
                        else if (changeType == PathChanges.MembershipChanged && possibleMembershipChanges.ContainsKey(internalPath))
                        {
                            // Already queued for validation.
                        }
                        else
                        {
                            if ((usnRecord.Reason & UsnChangeReasons.HardLinkChange) != 0 && changeType == PathChanges.Removed)
                            {
                                // See comment on <see cref="CheckAndMaybeInvalidateHardLinksChanges">.
                                possiblyHardLinkChanges.Add((internalPath, usnRecord));
                            }
                            else
                            {
                                handleChangedPath(changeType, expandedPath);
                                handledChanges.Add(usnRecord.FileId);
                            }
                        }
                    });
            }

            ScanningJournalResult result;

            if (readResponse.IsError)
            {
                switch (readResponse.Error.Status)
                {
                    case ErrorStatus.FailedToOpenVolumeHandle:
                        result = ScanningJournalResult.Fail(ScanningJournalStatus.FailedToOpenVolumeHandle, stats);
                        break;
                    case ErrorStatus.ProtocolError:
                        result = ScanningJournalResult.Fail(ScanningJournalStatus.ProtocolError, stats);
                        break;
                    default:
                        throw Contract.AssertFailure("Unreachable");
                }

                Contract.Assert(result != null);

                volumeTrackingSet.LogScanningJournalResult(result);
                handleChangedPathError?.Invoke(new ScanningJournalException(result));

                return result;
            }

            if (readResponse.Response.Status != ReadUsnJournalStatus.Success)
            {
                switch (readResponse.Response.Status)
                {
                    case ReadUsnJournalStatus.JournalNotActive:
                        result = ScanningJournalResult.Fail(ScanningJournalStatus.JournalNotActive, stats);
                        break;
                    case ReadUsnJournalStatus.JournalEntryDeleted:
                        result = ScanningJournalResult.Fail(ScanningJournalStatus.JournalEntryDeleted, stats);
                        break;
                    case ReadUsnJournalStatus.JournalDeleteInProgress:
                        result = ScanningJournalResult.Fail(ScanningJournalStatus.JournalDeleteInProgress, stats);
                        break;
                    case ReadUsnJournalStatus.InvalidParameter:
                        result = ScanningJournalResult.Fail(ScanningJournalStatus.InvalidParameter, stats);
                        break;
                    case ReadUsnJournalStatus.VolumeDoesNotSupportChangeJournals:
                        result = ScanningJournalResult.Fail(ScanningJournalStatus.VolumeDoesNotSupportChangeJournals, stats);
                        break;
                    default:
                        throw Contract.AssertFailure("Unreachable");
                }

                Contract.Assert(result != null);

                volumeTrackingSet.LogScanningJournalResult(result);
                handleChangedPathError?.Invoke(new ScanningJournalException(result));

                return result;
            }

            if (readResponse.Response.Timeout)
            {
                result = ScanningJournalResult.Fail(ScanningJournalStatus.Timeout, stats);

                volumeTrackingSet.LogScanningJournalResult(result);
                handleChangedPathError?.Invoke(new ScanningJournalException(result));

                return result;
            }

            using (stats.StartStopwatch(ReadJournalCounter.FalsePositiveValidationDuration))
            {
                // Double-check anti-dependency and enumeration invalidations (see remarks about false positives on the Enumerated and Absent flags).
                CheckAndMaybeInvalidateAntiDependencies(handleChangedPath, possiblyNewlyPresentPaths, stats);
                CheckAndMaybeInvalidateEnumerationDependencies(handleChangedPath, possibleMembershipChanges, stats);

                // Double-check hardlink changes (see remarks on CheckAndMaybeInvalidateHardLinksChanges).
                CheckAndMaybeInvalidateHardLinksChanges(handleChangedPath, possiblyHardLinkChanges, handledChanges, stats);
            }

            if (volumeTrackingSet.CheckpointProcessedChanges(readResponse.Response.NextUsn))
            {
                m_hasNewFileOrCheckpointData = true;
            }

            result = ScanningJournalResult.Success(stats);

            volumeTrackingSet.LogScanningJournalResult(result);
            handleChangedPathCompletion?.Invoke();

            return result;
        }

        #region False-positive change validation

        private struct InvalidateAntiDependencyStats
        {
            public int NumOfVerifiedToBeAbsent;
            public int NumOfFailedToRetrackAsNonExistent;
            public int NumOfFailedToProbe;
        }

        /// <summary>
        /// Given a list of possibly-newly-present paths, double-checks that they are actually newly present (re-tracks if not).
        /// </summary>
        /// <remarks>
        /// May not be called while a journal scan is in progress, since this mutates tracking state.
        /// </remarks>
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
        private void CheckAndMaybeInvalidateAntiDependencies(
            Action<PathChanges, string> handleChangedPath,
            HashSet<AbsolutePath> possiblyNewlyPresentPaths,
            CounterCollection<ReadJournalCounter> stats)
        {
            if (possiblyNewlyPresentPaths.Count == 0)
            {
                return;
            }

            var numOfMessages = 0;
            var numOfVerifiedToBeAbsent = 0;
            var numOfFailedToRetrackAsNonExistent = 0;
            var numOfFailedToProbe = 0;

            using (stats.StartStopwatch(ReadJournalCounter.ValidateAntiDependencies))
            {
                var listOfPossiblyNewlyPresentPaths = possiblyNewlyPresentPaths.ToList();
                var typesOfNewlyPresentPaths = new PathExistence?[listOfPossiblyNewlyPresentPaths.Count];
                var failedNonExistentPathTracks = new ConcurrentDictionary<AbsolutePath, bool>();
                var partitions = Partitioner.Create(0, listOfPossiblyNewlyPresentPaths.Count, Math.Max(1, s_maxConcurrencyForRecordProcessing));

                Parallel.ForEach(
                    partitions,
                    new ParallelOptions { MaxDegreeOfParallelism = s_maxConcurrencyForRecordProcessing },
                    () => default(InvalidateAntiDependencyStats),
                    (range, loopState, localStats) =>
                    {
                        var shouldLog = Volatile.Read(ref numOfMessages) < MaxMessagePerChangeValidationType;

                        for (int i = range.Item1; i < range.Item2; ++i)
                        {
                            var possiblyNewlyPresentPath = listOfPossiblyNewlyPresentPaths[i];
                            string expandedPath = possiblyNewlyPresentPath.ToString(m_internalPathTable);
                            typesOfNewlyPresentPaths[i] = null;

                            Possible<ProbeResult> possiblyProbed = TryProbeAndTrackPathInternal(
                                possiblyNewlyPresentPath, 
                                filter: ExistenceTrackingFilter.TrackIfNonexistent);

                            bool shouldEmitChange = true;
                            if (possiblyProbed.Succeeded)
                            {
                                ProbeResult probeResult = possiblyProbed.Result;
                                typesOfNewlyPresentPaths[i] = probeResult.Existence;

                                if (probeResult.Existence == PathExistence.Nonexistent)
                                {
                                    if (probeResult.PossibleTrackingResult.Succeeded)
                                    {
                                        shouldEmitChange = false;
                                        ++localStats.NumOfVerifiedToBeAbsent;

                                        if (shouldLog)
                                        {
                                            Interlocked.Increment(ref numOfMessages);
                                            Logger.Log.AntiDependencyValidationPotentiallyAddedButVerifiedAbsent(m_loggingContext, expandedPath);
                                        }
                                    }
                                    else
                                    {
                                        ++localStats.NumOfFailedToRetrackAsNonExistent;
                                        failedNonExistentPathTracks.TryAdd(possiblyNewlyPresentPath, true);

                                        if (shouldLog)
                                        {
                                            Interlocked.Increment(ref numOfMessages);
                                            Logger.Log.AntiDependencyValidationFailedRetrackPathAsNonExistent(
                                                m_loggingContext,
                                                expandedPath,
                                                probeResult.PossibleTrackingResult.Failure.DescribeIncludingInnerFailures());
                                        }
                                    }
                                }
                            }
                            else
                            {
                                ++localStats.NumOfFailedToProbe;

                                if (shouldLog)
                                {
                                    Interlocked.Increment(ref numOfMessages);
                                    Logger.Log.AntiDependencyValidationFailedProbePathToVerifyNonExistent(
                                        m_loggingContext,
                                        expandedPath,
                                        possiblyProbed.Failure.DescribeIncludingInnerFailures());
                                }
                            }

                            if (!shouldEmitChange)
                            {
                                listOfPossiblyNewlyPresentPaths[i] = AbsolutePath.Invalid;
                            }
                        }

                        return localStats;
                    },
                    localStats =>
                    {
                        Interlocked.Add(ref numOfVerifiedToBeAbsent, localStats.NumOfVerifiedToBeAbsent);
                        Interlocked.Add(ref numOfFailedToRetrackAsNonExistent, localStats.NumOfFailedToRetrackAsNonExistent);
                        Interlocked.Add(ref numOfFailedToProbe, localStats.NumOfFailedToProbe);
                    });

                for (int i = 0; i < listOfPossiblyNewlyPresentPaths.Count; ++i)
                {
                    var possiblyNewlyPresentPath = listOfPossiblyNewlyPresentPaths[i];

                    if (possiblyNewlyPresentPath.IsValid)
                    {
                        var typeOfNewlyPresentPath = typesOfNewlyPresentPaths[i];
                        var pathChange = PathChanges.NewlyPresent;

                        if (typeOfNewlyPresentPath.HasValue)
                        {
                            if (typeOfNewlyPresentPath.Value == PathExistence.ExistsAsDirectory)
                            {
                                pathChange = PathChanges.NewlyPresentAsDirectory;
                            }
                            else if (typeOfNewlyPresentPath.Value == PathExistence.ExistsAsFile)
                            {
                                pathChange = PathChanges.NewlyPresentAsFile;
                            }
                            else
                            {
                                if (!failedNonExistentPathTracks.ContainsKey(possiblyNewlyPresentPath))
                                {
                                    Contract.Assert(false, I($"Path has been determined to be present, but its path existence is {nameof(PathExistence.Nonexistent)}"));
                                }

                                // Failed to retrack the fact that path is non-existent; assume newly present.
                                pathChange = PathChanges.NewlyPresent;
                            }
                        }

                        // This is the notification we optimistically skipped prior (note that paths in possiblyNewlyPresentPaths had exactly this change reason).
                        string expandedPath = possiblyNewlyPresentPath.ToString(m_internalPathTable);
                        handleChangedPath(pathChange, expandedPath);
                    }
                }
            }

            stats.AddToCounter(ReadJournalCounter.ExistentialChangesSuppressedAfterVerificationCount, numOfVerifiedToBeAbsent);
            Logger.Log.AntiDependencyValidationStats(
                m_loggingContext,
                numOfVerifiedToBeAbsent,
                numOfFailedToRetrackAsNonExistent,
                numOfFailedToProbe,
                (int)stats.GetElapsedTime(ReadJournalCounter.ValidateAntiDependencies).TotalMilliseconds);
        }

        private struct InvalidateEnumerationDependencyStats
        {
            public int NumOfUnchanges;
            public int NumOfFailedToRetrack;
            public int NumOfFailedToOpenAndEnumerate;
        }

        /// <summary>
        /// Given a list of possibly-modified directories and their prior membership fingerprints, double-checks that they are actually different (re-tracks if not).
        /// </summary>
        /// <remarks>
        /// May not be called while a journal scan is in progress, since this mutates tracking state.
        /// </remarks>
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
        private void CheckAndMaybeInvalidateEnumerationDependencies(
            Action<PathChanges, string> handleChangedPath,
            Dictionary<AbsolutePath, DirectoryMembershipTrackingFingerprint> possibleMembershipChanges,
            CounterCollection<ReadJournalCounter> stats)
        {
            if (possibleMembershipChanges.Count == 0)
            {
                return;
            }

            var numOfMessages = 0;
            var numOfUnchanges = 0;
            var numOfFailedToRetrack = 0;
            var numOfFailedToOpenAndEnumerate = 0;

            using (stats.StartStopwatch(ReadJournalCounter.ValidateEnumerationDependencies))
            {
                var listOfPossibleMembershipChanges = possibleMembershipChanges.ToList();
                var partitions = Partitioner.Create(0, listOfPossibleMembershipChanges.Count, Math.Max(1, s_maxConcurrencyForRecordProcessing));
                var emitChanges = new bool[listOfPossibleMembershipChanges.Count];

                Parallel.ForEach(
                    partitions,
                    new ParallelOptions { MaxDegreeOfParallelism = s_maxConcurrencyForRecordProcessing },
                    () => default(InvalidateEnumerationDependencyStats),
                    (range, loopState, localStats) =>
                    {
                        var shouldLog = Volatile.Read(ref numOfMessages) < MaxMessagePerChangeValidationType;

                        for (int i = range.Item1; i < range.Item2; ++i)
                        {
                            var directoryAndFingerprint = listOfPossibleMembershipChanges[i];
                            string expandedPath = directoryAndFingerprint.Key.ToString(m_internalPathTable);

                            Contract.Assume(
                                directoryAndFingerprint.Value != DirectoryMembershipTrackingFingerprint.Conflict,
                                "Should be excluded by TryProcessChanges");

                            Possible<EnumerationResult> possiblyEnumerated =
                                TryEnumerateDirectoryAndTrackMembership(
                                    expandedPath,
                                    (name, attributes) => { },
                                    fingerprintFilter: directoryAndFingerprint.Value);

                            bool shouldEmitChange = true;

                            if (possiblyEnumerated.Succeeded)
                            {
                                EnumerationResult enumerationResult = possiblyEnumerated.Result;
                                if (enumerationResult.Existence != PathExistence.Nonexistent)
                                {
                                    if (enumerationResult.PossibleTrackingResult.Succeeded)
                                    {
                                        if (enumerationResult.PossibleTrackingResult.Result == ConditionalTrackingResult.Tracked)
                                        {
                                            shouldEmitChange = false;
                                            ++localStats.NumOfUnchanges;

                                            if (shouldLog)
                                            {
                                                Interlocked.Increment(ref numOfMessages);
                                                Logger.Log.EnumerationDependencyValidationPotentiallyAddedOrRemovedDirectChildrenButVerifiedUnchanged(
                                                    m_loggingContext,
                                                    expandedPath);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        ++localStats.NumOfFailedToRetrack;

                                        if (shouldLog)
                                        {
                                            Interlocked.Increment(ref numOfMessages);
                                            Logger.Log.EnumerationDependencyValidationFailedRetrackUnchangedDirectoryForMembershipChanges(
                                                m_loggingContext,
                                                expandedPath,
                                                enumerationResult.PossibleTrackingResult.Failure.DescribeIncludingInnerFailures());
                                        }
                                    }
                                }
                            }
                            else
                            {
                                ++localStats.NumOfFailedToOpenAndEnumerate;

                                if (shouldLog)
                                {
                                    Interlocked.Increment(ref numOfMessages);
                                    Logger.Log.EnumerationDependencyValidationFailedToOpenOrEnumerateDirectoryForMembershipChanges(
                                        m_loggingContext,
                                        expandedPath,
                                        possiblyEnumerated.Failure.DescribeIncludingInnerFailures());
                                }
                            }

                            emitChanges[i] = shouldEmitChange;
                        }

                        return localStats;
                    },
                    localStats =>
                    {
                        Interlocked.Add(ref numOfUnchanges, localStats.NumOfUnchanges);
                        Interlocked.Add(ref numOfFailedToOpenAndEnumerate, localStats.NumOfFailedToOpenAndEnumerate);
                        Interlocked.Add(ref numOfFailedToRetrack, localStats.NumOfFailedToRetrack);
                    });

                for (int i = 0; i < listOfPossibleMembershipChanges.Count; ++i)
                {
                    if (emitChanges[i])
                    {
                        string expandedPath = listOfPossibleMembershipChanges[i].Key.ToString(m_internalPathTable);

                        // This is the notification we optimistically skipped prior (note that paths in possibleMembershipChanges had exactly this change reason).
                        handleChangedPath(PathChanges.MembershipChanged, expandedPath);
                    }
                }
            }

            stats.AddToCounter(ReadJournalCounter.ExistentialChangesSuppressedAfterVerificationCount, numOfUnchanges);
            Logger.Log.EnumerationDependencyValidationStats(
                m_loggingContext,
                numOfUnchanges,
                numOfFailedToRetrack,
                numOfFailedToOpenAndEnumerate,
                (int)stats.GetElapsedTime(ReadJournalCounter.ValidateEnumerationDependencies).TotalMilliseconds);
        }

        private struct InvalidateHardLinkStats
        {
            public int NumOfUnchanges;
            public int NumOfFileIdChanges;
            public int NumOfFailedToRetrack;
            public int NumOfNonExistent;
            public int NumOfFailedToOpenAndTrack;
        }

        /// <summary>
        /// Checks and possibly invalidates hard link changes.
        /// </summary>
        /// <remarks>
        /// Consider the scenario where a pip produces two output files (1.txt and 2.txt) with the same content (empty, perhaps). The first clean build will
        /// give you the following entries in the journal:
        ///
        ///   Usn               : 1791868688
        ///   File name         : 1.txt
        ///   Reason            : 0x80010000: Hard link change | Close
        ///   File ID           : 0000000000000000000600000006a0cf
        ///   Parent file ID    : 0000000000000000000700000006a0f9
        ///
        ///   Usn               : 1791868928
        ///   File name         : 1E57CF2792A900D06C1CDFB3C453F35BC86F72788AA9724C96C929D1CC6B456A00.blob
        ///   Reason            : 0x80000000: Close
        ///   File ID           : 0000000000000000000600000006a0cf
        ///   Parent file ID    : 0000000000000000000600000006a0d2
        ///
        ///   Usn               : 1791869136
        ///   File name         : 2.txt
        ///   Reason            : 0x80010000: Hard link change | Close
        ///   File ID           : 0000000000000000000600000006a0cf
        ///   Parent file ID    : 0000000000000000000700000006a0f9
        ///
        ///   Usn               : 1791869208
        ///   File name         : 1E57CF2792A900D06C1CDFB3C453F35BC86F72788AA9724C96C929D1CC6B456A00.blob
        ///   Reason            : 0x80000000: Close
        ///   File ID           : 0000000000000000000600000006a0cf
        ///   Parent file ID    : 0000000000000000000600000006a0d2
        ///
        /// BuildXL in <see cref="SingleVolumeFileChangeTrackingSet.m_recordsByFileId"/> tracks it as follows:
        ///     0000000000000000000600000006a0cf |-> (1.txt, usn: 1791868928); (2.txt, usn: 1791869208)
        ///
        /// In the second no-op build BuildXL scans the above sequence. For the first and second entries, nothing from <see cref="SingleVolumeFileChangeTrackingSet.m_recordsByFileId"/> gets
        /// notified (as change). But on scanning the third entry, i.e., the entry with usn 1791869136, the entry (1.txt, usn: 1791868928)
        /// is marked impacted, and 1.txt is notified.
        ///
        /// In general if a pip produces N outputs of the same content, then BuildXL will say that N-1 of them have changed in the second build,
        /// although the second build is a no-op build.
        ///
        /// Deletions of files in cache (or evictions) do not fall into this invalidation. Recall that the files in cache and in the output folder
        /// can be hard links to the same blob with the same file id. Deletion of files in cache can happen if the user explicitly wipe out the cache, or
        /// if the cache starts purging because it has reached its soft limit. If a hard link in the cache gets evicted, the hard link in the output folder
        /// is not affected because their containers have different file id. See <see cref="SingleVolumeFileChangeTrackingSet.PopImpactedRecordsIfPresent"/>.
        ///
        /// The following method tries to check if the hard links indeed changed. If not, then it will retrack them.
        /// </remarks>
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
        private void CheckAndMaybeInvalidateHardLinksChanges(
            Action<PathChanges, string> handleChangedPath,
            List<(AbsolutePath path, UsnRecord usnRecord)> hardLinkChanges,
            HashSet<FileId> potentiallyDataChangedFileIds,
            CounterCollection<ReadJournalCounter> stats)
        {
            if (hardLinkChanges.Count == 0)
            {
                return;
            }

            var numOfMessages = 0;
            var numOfUnchanges = 0;
            var numOfFileIdChanges = 0;
            var numOfFailedToRetrack = 0;
            var numOfNonExistent = 0;
            var numOfFailedToOpenAndTrack = 0;

            using (stats.StartStopwatch(ReadJournalCounter.ValidateHardlinkChanges))
            {
                var partitions = Partitioner.Create(0, hardLinkChanges.Count, Math.Max(1, s_maxConcurrencyForRecordProcessing));
                var emitChanges = new bool[hardLinkChanges.Count];

                Parallel.ForEach(
                    partitions,
                    new ParallelOptions { MaxDegreeOfParallelism = s_maxConcurrencyForRecordProcessing },
                    () => default(InvalidateHardLinkStats),
                    (range, loopState, localStats) =>
                    {
                        var shouldLog = Volatile.Read(ref numOfMessages) < MaxMessagePerChangeValidationType;

                        for (int i = range.Item1; i < range.Item2; ++i)
                        {
                            var hardLinkPathAndUsnRecord = hardLinkChanges[i];
                            string expandedPath = hardLinkPathAndUsnRecord.Item1.ToString(m_internalPathTable);

                            if (potentiallyDataChangedFileIds.Contains(hardLinkPathAndUsnRecord.Item2.FileId))
                            {
                                emitChanges[i] = true;
                                continue;
                            }

                            Possible<TrackAndOpenResult> possiblyHardLinkExist =
                                TryOpenAndTrackPathInternal(
                                    hardLinkPathAndUsnRecord.Item1,
                                    access: FileDesiredAccess.GenericRead,
                                    filter: ExistenceTrackingFilter.TrackIfExistent,
                                    existentFileFilter: fileHandle =>
                                                        {
                                                            // Track only if file id is the same.
                                                            var possiblyVersionFileIdentity = VersionedFileIdentity.TryQuery(fileHandle);
                                                            return possiblyVersionFileIdentity.Succeeded &&
                                                                   possiblyVersionFileIdentity.Result.FileId == hardLinkPathAndUsnRecord.Item2.FileId;
                                                        });

                            bool shouldEmitChange = true;
                            if (possiblyHardLinkExist.Succeeded)
                            {
                                using (TrackAndOpenResult trackAndOpenResult = possiblyHardLinkExist.Result)
                                {
                                    if (trackAndOpenResult.Existent)
                                    {
                                        if (trackAndOpenResult.PossibleTrackingResult.Succeeded)
                                        {
                                            if (trackAndOpenResult.PossibleTrackingResult.Result == ConditionalTrackingResult.Tracked)
                                            {
                                                shouldEmitChange = false;
                                                ++localStats.NumOfUnchanges;

                                                if (shouldLog)
                                                {
                                                    Interlocked.Increment(ref numOfMessages);
                                                    Logger.Log.HardLinkValidationPotentiallyChangedButVerifiedUnchanged(
                                                        m_loggingContext,
                                                        expandedPath);
                                                }
                                            }
                                            else
                                            {
                                                ++localStats.NumOfFileIdChanges;

                                                if (shouldLog)
                                                {
                                                    Interlocked.Increment(ref numOfMessages);
                                                    Logger.Log.HardLinkValidationHardLinkChangedBecauseFileIdChanged(
                                                        m_loggingContext,
                                                        expandedPath);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            ++localStats.NumOfFailedToRetrack;

                                            if (shouldLog)
                                            {
                                                Interlocked.Increment(ref numOfMessages);
                                                Logger.Log.HardLinkValidationFailedRetrackUnchangedHardLink(
                                                    m_loggingContext,
                                                    expandedPath,
                                                    trackAndOpenResult.PossibleTrackingResult.Failure.DescribeIncludingInnerFailures());
                                            }
                                        }
                                    }
                                    else
                                    {
                                        ++localStats.NumOfNonExistent;

                                        if (shouldLog)
                                        {
                                            Interlocked.Increment(ref numOfMessages);
                                            Logger.Log.HardLinkValidationFailedToOpenHardLinkDueToNonExistent(
                                                m_loggingContext,
                                                expandedPath);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                ++localStats.NumOfFailedToOpenAndTrack;

                                if (shouldLog)
                                {
                                    Interlocked.Increment(ref numOfMessages);
                                    Logger.Log.HardLinkValidationFailedToOpenOrTrackHardLink(
                                        m_loggingContext,
                                        expandedPath,
                                        possiblyHardLinkExist.Failure.DescribeIncludingInnerFailures());
                                }
                            }

                            emitChanges[i] = shouldEmitChange;
                        }

                        return localStats;
                    },
                    localStats =>
                    {
                        Interlocked.Add(ref numOfUnchanges, localStats.NumOfUnchanges);
                        Interlocked.Add(ref numOfFailedToRetrack, localStats.NumOfFailedToRetrack);
                        Interlocked.Add(ref numOfFailedToOpenAndTrack, localStats.NumOfFailedToOpenAndTrack);
                        Interlocked.Add(ref numOfFileIdChanges, localStats.NumOfFileIdChanges);
                        Interlocked.Add(ref numOfNonExistent, localStats.NumOfNonExistent);
                    });

                for (int i = 0; i < hardLinkChanges.Count; ++i)
                {
                    if (emitChanges[i])
                    {
                        string expandedPath = hardLinkChanges[i].Item1.ToString(m_internalPathTable);
                        handleChangedPath(PathChanges.Removed, expandedPath);
                    }
                }
            }

            stats.AddToCounter(ReadJournalCounter.ExistentialChangesSuppressedAfterVerificationCount, numOfUnchanges);
            Logger.Log.HardLinkValidationStats(
                m_loggingContext,
                numOfUnchanges,
                numOfFileIdChanges,
                numOfFailedToRetrack,
                numOfNonExistent,
                numOfFailedToOpenAndTrack,
                (int)stats.GetElapsedTime(ReadJournalCounter.ValidateHardlinkChanges).TotalMilliseconds);
        }

        #endregion

        #region Tracking existent files (may mark paths with the 'Tracked' flag)

        /// <summary>
        /// Attempts to add the provided change-tracking information to the change tracking set.
        /// The file's current version may be later than the current checkpoint of the change tracking set;
        /// if so, changes between the checkpoint and the current version of the file are potentially (and ideally) excluded.
        /// </summary>
        /// <returns>
        /// If a failure is returned, changes to the specified file may not be detected.
        /// Failure may indicate that the file is on a volume that does not have a supported or enabled change journal.
        /// </returns>
        public Possible<FileChangeTrackingSubscription> TryTrackChangesToFile(
            SafeFileHandle handle,
            string path,
            VersionedFileIdentity? maybeIdentity = null,
            TrackingUpdateMode updateMode = TrackingUpdateMode.Preserve)
        {
            Contract.Requires(handle != null);

            using (Counters.StartStopwatch(FileChangeTrackingCounter.TryTrackChangesToFileTime))
            {
                AbsolutePath internalPath = AbsolutePath.Create(m_internalPathTable, path);
                return TryTrackChangesToFileInternal(handle, internalPath, maybeIdentity, updateMode);
            }
        }

        private Possible<FileChangeTrackingSubscription> TryTrackChangesToFileInternal(
            SafeFileHandle handle,
            AbsolutePath internalPath,
            VersionedFileIdentity? maybeIdentity = null,
            TrackingUpdateMode updateMode = TrackingUpdateMode.Preserve)
        {
            Contract.Requires(handle != null);
            Contract.Requires(internalPath.IsValid);

            using (Counters.StartStopwatch(FileChangeTrackingCounter.TryTrackChangesToFileInternalTime))
            {
                VersionedFileIdentity identity;
                if (maybeIdentity.HasValue)
                {
                    identity = maybeIdentity.Value;
                }
                else
                {
                    using (Counters.StartStopwatch(FileChangeTrackingCounter.TryEstablishStrongTime))
                    {
                        Possible<VersionedFileIdentity, Failure<VersionedFileIdentity.IdentityUnavailabilityReason>> possibleIdentity =
                            VersionedFileIdentity.TryEstablishStrong(handle, flush: false);
                        if (!possibleIdentity.Succeeded)
                        {
                            return possibleIdentity.Failure.Annotate("Tracking failed - unable to query the identity of a file");
                        }

                        identity = possibleIdentity.Result;
                    }
                }

                // Note that we allow e.g. IdentityKind.Anonymous to flow into here (e.g. from FileContentTable.RecordContentHashAsync) and
                // treat it gracefully, just like if TryEstablishStrong fails above.
                // TODO: Maybe WeakUsn should not be allowed here; but there are existing call sites (perhaps just TryOpenAndTrackPathInternal) which are
                //       passing in weak USNs, and the validity of that vs. perf should be revisited.
                if (!identity.Kind.IsWeakOrStrong())
                {
                    return new Failure<string>(I($"Tracking failed - unsupported identity kind {identity.Kind:G}"));
                }

                SingleVolumeFileChangeTrackingSet volumeTrackingSet;
                if (!m_perVolumeChangeTrackingSets.TryGetValue(identity.VolumeSerialNumber, out volumeTrackingSet))
                {
                    return new Failure<string>(I($"Tracking failed - unknown volume {identity.VolumeSerialNumber:X16}"));
                }

                Possible<FileChangeTrackingSubscription> possibleSubscription = volumeTrackingSet.TryTrackChangesToFile(updateMode, handle, internalPath, identity);
                Volatile.Write(ref m_hasNewFileOrCheckpointData, true);

                return possibleSubscription;
            }
        }

        #endregion

        #region Tracking the immediate membership of existent directories (may mark paths with the 'Enumerated' flag)

        /// <summary>
        /// Annotates an existing subscription (for some path) to also track its child membership (assuming the subscribed path refers to a directory).
        /// The membership of the directory will be invalidated if a name is added or removed directly inside the directory (i.e., when <c>FindFirstFile</c>
        /// and <c>FindNextFile</c> would see a different set of names).
        /// For correct tracking, enumeration of the directory must have occurred *after* the subscription was established.
        /// </summary>
        public void TrackDirectoryMembership(FileChangeTrackingSubscription subscription, DirectoryMembershipTrackingFingerprint membershipFingerprint)
        {
            const int ConflictDirectoryMembershipFingerprintMaxCount = 10;

            // In addition to Enumerated (clearly the intent), this path is now also a Container in the sense that it has a child subscription (enumeration);
            // paths with such children cannot be superseded.
            m_internalPathTable.SetFlags(subscription.ChangeTrackingSetInternalPath.Value, Container | Enumerated);

            // We try to avoid a high false positive rate for enumeration dependencies by recording a single tracked fingerprint for each enumerated directory.
            // Since we don't have at hand some USN as of tracking time that is higher than all existing child USNs, tracking a directory after creating it
            // would (without double-checking fingerprint) always generate an invalidation on the next scan (due to the create event). That could be addressed
            // differently, but the fingerprinting approach is additionally robust against creation (and later deletion) of temporary file names (saving files
            // in Visual Studio is a very relevant example).
            DirectoryMembershipTrackingFingerprint result = m_directoryMembershipTrackingFingerprints.AddOrUpdate(
                subscription.ChangeTrackingSetInternalPath,
                membershipFingerprint,
                (path, existingFingerprint) => existingFingerprint == membershipFingerprint
                    ? existingFingerprint
                    : DirectoryMembershipTrackingFingerprint.Conflict);

            if (result == DirectoryMembershipTrackingFingerprint.Conflict)
            {
                Counters.IncrementCounter(FileChangeTrackingCounter.ConflictDirectoryMembershipFingerprintCount);
                if (Counters.GetCounterValue(FileChangeTrackingCounter.ConflictDirectoryMembershipFingerprintCount) <= ConflictDirectoryMembershipFingerprintMaxCount)
                {
                    Logger.Log.ConflictDirectoryMembershipFingerprint(
                        m_loggingContext,
                        subscription.ChangeTrackingSetInternalPath.ToString(m_internalPathTable));
                }
            }
        }

        /// <summary>
        /// Result of successfully enumerating a path with <see cref="FileChangeTrackingSet.TryEnumerateDirectoryAndTrackMembership"/>.
        /// This represents a calculated <see cref="Fingerprint"/> of the directory, though tracking the directory may have failed
        /// (this is captured in <see cref="PossibleTrackingResult"/>). The directory may not exist, or may actually be a file; see <see cref="Existence"/>.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public readonly struct EnumerationResult
        {
            /// <summary>
            /// Fingerprint of the enumerated path (changes when membership changes).
            /// </summary>
            public readonly DirectoryMembershipTrackingFingerprint Fingerprint;

            /// <summary>
            /// Existence of the path that was enumerated (note that it may have been a file).
            /// </summary>
            public readonly PathExistence Existence;

            /// <summary>
            /// Result of attempting to track the enumerated path (or its absence).
            /// Indicates if tracking was actually attempted, or skipped (such as due to a mismatched fingerprint filter).
            /// </summary>
            public readonly Possible<ConditionalTrackingResult> PossibleTrackingResult;

            /// <nodoc />
            public EnumerationResult(DirectoryMembershipTrackingFingerprint fingerprint, PathExistence existence, Possible<ConditionalTrackingResult> trackingResult)
            {
                Existence = existence;
                Fingerprint = fingerprint;
                PossibleTrackingResult = trackingResult;
            }
        }

        /// <summary>
        /// Combined operation of opening and tracking a directory (or its absence), enumerating it, and then tracking changes to that enumeration result (its membership).
        /// The membership of the directory will be invalidated if a name is added or removed directly inside the directory (i.e., when <c>FindFirstFile</c>
        /// and <c>FindNextFile</c> would see a different set of names).
        /// Tracking the enumeration result can be conditioned on the computed fingerprint matching <paramref name="fingerprintFilter"/> (i.e., 'retrack
        /// membership if it is the same as a prior fingerprint').
        /// </summary>
        /// <remarks>
        /// The returned value represent two levels of failure:
        /// - Opening or enumerating the specified directory may fail (top-level Possible is failed)
        /// - Opening and enumerating succeeded, but tracking failed (see <see cref="EnumerationResult.PossibleTrackingResult"/>).
        /// This is needed since consumers like <see cref="FileChangeTracker"/> need to observe tracking failures gracefully while still using the enumeration result.
        /// </remarks>
        public Possible<EnumerationResult> TryEnumerateDirectoryAndTrackMembership(
            string path,
            Action<string, FileAttributes> handleEntry,
            DirectoryMembershipTrackingFingerprint? fingerprintFilter = null)
        {
            using (Counters.StartStopwatch(FileChangeTrackingCounter.TryEnumerateDirectoryAndTrackMembershipTime))
            {
                AbsolutePath internalPath = AbsolutePath.Create(m_internalPathTable, path);

                Possible<(DirectoryMembershipTrackingFingerprint, PathExistence)>? possibleEnumeration = null;

                // enumerateAndCheckFingerprint is called if we managed to open a handle to 'path'. At that point we know it exists, but it may be a file.
                // The returned bool indicates if the existent path should be tracked or not.
                // Since TryOpenAndTrackPath may make multiple snapshot attempts, this may be called multiple times.
                Func<SafeFileHandle, bool> enumerateAndCheckFingerprint =
                    handle =>
                    {
                        var possibleFingerprintResult = DirectoryMembershipTrackingFingerprinter.ComputeFingerprint(path, handleEntry);
                        if (!possibleFingerprintResult.Succeeded)
                        {
                            possibleEnumeration = possibleFingerprintResult.Failure;
                            return false;
                        }

                        DirectoryMembershipTrackingFingerprint accumulator = possibleFingerprintResult.Result.Fingerprint;
                        int memberCount = possibleFingerprintResult.Result.MemberCount;
                        PathExistence existence = possibleFingerprintResult.Result.PathExistence;

                        Logger.Log.ChangeDetectionComputedDirectoryMembershipTrackingFingerprint(
                            m_loggingContext,
                            path,
                            memberCount,
                            accumulator.ToString());

                        possibleEnumeration = (accumulator, existence);

                        // On success, tracking is conditional on the fingerprint filter.
                        return !fingerprintFilter.HasValue || fingerprintFilter.Value == accumulator;
                    };

                Possible<TrackAndOpenResult> possibleTrackAndOpenResult =
                    TryOpenAndTrackPathInternal(
                        internalPath,
                        FileDesiredAccess.GenericRead,
                        ExistenceTrackingFilter.TrackAlways,
                        existentFileFilter: enumerateAndCheckFingerprint);

                return TrackDirectoryMembership(possibleTrackAndOpenResult, possibleEnumeration, internalPath);
            }
        }

        private Possible<EnumerationResult> TrackDirectoryMembership(
            Possible<TrackAndOpenResult> possibleTrackAndOpenResult,
            Possible<(DirectoryMembershipTrackingFingerprint, PathExistence)>? possibleEnumeration,
            AbsolutePath internalPath)
        {
            return possibleTrackAndOpenResult.Then(
                trackAndOpenResult =>
                {
                    using (trackAndOpenResult)
                    {
                        if (trackAndOpenResult.Existent)
                        {
                            Contract.Assume(
                                possibleEnumeration.HasValue,
                                "possibleEnumeration should be set in all paths of enumerateAndCheckFingerprint (should be called whenever existent)");

                            return possibleEnumeration.Value.Then(
                                enumeration =>
                                {
                                    if (trackAndOpenResult.PossibleTrackingResult.Succeeded &&
                                        trackAndOpenResult.PossibleTrackingResult.Result == ConditionalTrackingResult.Tracked)
                                    {
                                        // TODO: trackAndOpenResult should have a subscription on it.
                                        //       We have an existent path and tracking succeeded, so we can safely invent one here.
                                        TrackDirectoryMembership(new FileChangeTrackingSubscription(internalPath), enumeration.Item1);
                                    }

                                    // possibleEnumeration contains a (fingerprint, file-or-directory) pair.
                                    // trackAndOpenResult.PossibleTrackingResult accounts for tracking success or failure (happens after the pair is determined,
                                    // in TryOpenAndTrackPathInternal).
                                    return new EnumerationResult(enumeration.Item1, enumeration.Item2, trackAndOpenResult.PossibleTrackingResult);
                                });
                        }
                        else
                        {
                            // possibleEnumeration is unusable (but may be set, since enumerateAndCheckFingerprint may have been called one or more times).
                            return new EnumerationResult(
                                DirectoryMembershipTrackingFingerprint.Absent,
                                PathExistence.Nonexistent,
                                trackAndOpenResult.PossibleTrackingResult);
                        }
                    }
                });
        }

        /// <summary>
        /// Tracks directory membership given directory path and all its members.
        /// </summary>
        public Possible<EnumerationResult> TryTrackDirectoryMembership(
            string path,
            IReadOnlyList<(string, FileAttributes)> members)
        {
            AbsolutePath internalPath = AbsolutePath.Create(m_internalPathTable, path);
            DirectoryMembershipTrackingFingerprint fingerprint = DirectoryMembershipTrackingFingerprinter.ComputeFingerprint(members);

            Possible<TrackAndOpenResult> possibleTrackAndOpenResult = TryOpenAndTrackPathInternal(
                internalPath,
                FileDesiredAccess.GenericRead,
                ExistenceTrackingFilter.TrackAlways);

            if (!possibleTrackAndOpenResult.Succeeded)
            {
                return possibleTrackAndOpenResult.Failure;
            }

            Possible<(DirectoryMembershipTrackingFingerprint, PathExistence)>? possibleEnumeration =
                (
                    fingerprint,
                    possibleTrackAndOpenResult.Result.Existent ? PathExistence.ExistsAsDirectory : PathExistence.Nonexistent);

            return TrackDirectoryMembership(possibleTrackAndOpenResult, possibleEnumeration, internalPath);
        }

        #endregion

        #region Tracking path existence (may mark paths with the 'Absent' flag)

        /// <summary>
        /// Tracks a non-existent relative path chain from a tracked parent root.
        /// If trackedParentPath = 'C:\foo' and relativeAbsentPath = 'a\b\c'
        /// Then the following paths are tracked as absent: 'C:\foo\a', 'C:\foo\a\b', 'C:\foo\a\b\c'.
        /// ADVANCED. Use with care. This should only be called if the relative has been validated as non-existent
        /// because the parent path is non-existent or enumerated and the child path was non-existent
        /// </summary>
        internal bool TrackAbsentRelativePath(string trackedParentPath, string absentRelativePath)
        {
            const int MaxUntrackedParentPathOnTrackingAbsentRelativePath = 5;

            var parentPath = AbsolutePath.Create(m_internalPathTable, trackedParentPath);

            HierarchicalNameTable.NameFlags existingParentPathFlags = m_internalPathTable.GetContainerAndFlags(parentPath.Value).Item2;
            HierarchicalNameTable.NameFlags tracked = Tracked | Absent;
            if ((existingParentPathFlags & tracked) == 0)
            {
                if (Interlocked.Increment(ref m_untrackedParentPathOnTrackingAbsentRelativePathCount) <= MaxUntrackedParentPathOnTrackingAbsentRelativePath)
                {
                    Logger.Log.ChangeDetectionParentPathIsUntrackedOnTrackingAbsentRelativePath(m_loggingContext, trackedParentPath, absentRelativePath);
                }

                // BUG: Parent path has not been tracked.
                // TODO: Fix me.
                return false;
            }

            var childPath = parentPath.Combine(m_internalPathTable, RelativePath.Create(m_internalPathTable.StringTable, absentRelativePath));
            TrackAbsentPathBetween(parentPath: parentPath, childPath: childPath);

            return true;
        }

        private void TrackAbsentPathBetween(AbsolutePath parentPath, AbsolutePath childPath)
        {
            Contract.Requires(childPath.IsValid);

            // Ensure that everything between the from path and the to path is marked as absent.
            // Since we walk up the path tree in marking things absent, we can stop on the first path already marked absent.
            for (AbsolutePath absentPath = childPath; absentPath != parentPath; absentPath = absentPath.GetParent(m_internalPathTable))
            {
                if (!absentPath.IsValid)
                {
                    Contract.Assume(false, I($"Path between {nameof(parentPath)} to {nameof(childPath)}, including {nameof(childPath)} should be a valid path."));
                }

                if (!m_internalPathTable.SetFlags(absentPath.Value, Absent))
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Annotates an existing subscription (for some path) to also track the absence of a specified child path.
        /// Correct tracking requires two considerations:
        /// - The existence probe which concluded absence of the child path must have occurred *after* the subscription was established.
        /// - At the time of the probe, the subscription path is the longest prefix of <paramref name="childPath"/> that does exist
        ///   (e.g. given a subscription for D\, one cannot specify an absent path D\E\F if E exists)
        /// </summary>
        /// <remarks>
        /// This is private since the requirements or correct tracking are very precise. The requirements are satisfied by <see cref="TryProbeAndTrackPath"/>
        /// since it walks up from the probe target (second requirement) and then double-checks existence of the immediate child path (first requirement).
        /// </remarks>
        private void TrackAbsentChildPath(FileChangeTrackingSubscription subscriptionForExistentPath, PathExistence pathExistence, AbsolutePath childPath)
        {
            Contract.Requires(subscriptionForExistentPath.IsValid);
            Contract.Requires(childPath.IsValid);
            Contract.Requires(pathExistence != PathExistence.Nonexistent);

            // Since this path will have dependent children (marked Absent), it is a 'container' and so may not be superseded (that orphan its children).
            if (pathExistence == PathExistence.ExistsAsDirectory)
            {
                // Some tool likes probing path D/f.txt, where D is in fact a file. If D is marked as 'container', then it may not be superseded.
                // This can cause overbuild. Consider the following scenario (found in Office). MsBuild probes D/f.txt and D/g.txt where D is
                // an output file. (Note that Detours has to report D/f.txt and D/g.txt, otherwise it may not report denied write-access if MsBuild
                // tries to write D/h.txt before writing D.) Because D has not been flushed both tracking of D/f.txt and D/g.txt can result in different
                // USNs for D. Without loss of generality, suppose that when tracking D/f.txt, D gets the smaller USN usn1, and that tracking is recorded
                // first. Suppose further that both trackings are using preserve-mode. The USN usn2 of D obtained during tracking of D/g.txt is not recorded
                // due to the preserve-mode. Eventually, a new record for D, with usn3, will be added during the post-process of D as an output file.
                // Moreover, usn3 is also recorded in the supresession limit for D because during the post-process D is added with the supersede mode.
                //
                // In the next build without any change, due to usn2, this tracker detects some changes to D wrt. the usn1 record. But because D is marked 'container', 
                // the supersession limit is ignored, and BuildXL reports some data/metadata change on D, although no changes have occured.
                m_internalPathTable.SetFlags(subscriptionForExistentPath.ChangeTrackingSetInternalPath.Value, Container);
            }

            TrackAbsentPathBetween(subscriptionForExistentPath.ChangeTrackingSetInternalPath, childPath);
        }

        /// <summary>
        /// Annotates and also track the absence of a specified path.
        /// </summary>
        private void TrackAbsentPath(AbsolutePath path)
        {
            TrackAbsentPathBetween(AbsolutePath.Invalid, path);
        }

        /// <summary>
        /// Given a path already marked absent, marks some child path (and all intermediate paths) as also absent.
        /// Unlike <see cref="TrackAbsentChildPath"/> (which has elaborate preconditions involving a subscription for some existent parent path), we have in hand
        /// an already-absent path (inductively, <see cref="TrackAbsentChildPath"/> was called somewhere above, so all preconditions are already met).
        /// </summary>
        private bool ExtendAbsentPath(AbsolutePath existingAbsentPath, AbsolutePath absentChildPath)
        {
            Contract.Requires(existingAbsentPath.IsValid);
            Contract.Requires(absentChildPath.IsValid);

            HierarchicalNameTable.NameFlags existingAbsentPathFlags = m_internalPathTable.GetContainerAndFlags(existingAbsentPath.Value).Item2;

            if ((existingAbsentPathFlags & Absent) == 0)
            {
                // Although the same condition is checked by the caller, some unknown race may clear the absent flag.
                // See Bug #936997
                return false;
            }

            for (AbsolutePath absentPath = absentChildPath; absentPath != existingAbsentPath; absentPath = absentPath.GetParent(m_internalPathTable))
            {
                Contract.Assume(absentPath.IsValid, "absentChildPath is supposed to be a child ofexistingAbsentPath, so we should reach it when walking up.");

                if (!m_internalPathTable.SetFlags(absentPath.Value, Absent))
                {
                    break;
                }
            }

            return true;
        }

        /// <summary>
        /// Indicates whether or not existence, non-existence, or possibly both should be tracked as part of a tracking probe (<see cref="FileChangeTrackingSet.TryProbeAndTrackPath"/>)
        /// </summary>
        [Flags]
        [SuppressMessage("Microsoft.Design", "CA1008:EnumsShouldHaveZeroValue")]
        [SuppressMessage("Microsoft.Naming", "CA1714:FlagsEnumsShouldHavePluralNames")]
        public enum ExistenceTrackingFilter
        {
            /// <summary>
            /// Don't track in any case (but still probe).
            /// </summary>
            TrackNever = 0,

            /// <summary>
            /// Track if the specified path exists (change or deletion will be detected).
            /// </summary>
            TrackIfExistent = 1,

            /// <summary>
            /// Track if the specified path does not exist (creation will be detected).
            /// </summary>
            TrackIfNonexistent = 2,

            /// <summary>
            /// Track always. Equivalent to <see cref="TrackIfExistent"/> combined with <see cref="TrackIfNonexistent"/>.
            /// </summary>
            TrackAlways = TrackIfExistent | TrackIfNonexistent,
        }

        /// <summary>
        /// Indication of if tracking was successful, or skipped due to a <see cref="ExistenceTrackingFilter"/>.
        /// </summary>
        public enum ConditionalTrackingResult
        {
            /// <summary>
            /// Path did not meet the filter criteria, so tracking was skipped.
            /// </summary>
            SkippedDueToFilter,

            /// <summary>
            /// The filter allowed for tracking, and tracking succeeded.
            /// </summary>
            Tracked,
        }

        /// <summary>
        /// Result of successfully probing a path with <see cref="FileChangeTrackingSet.TryProbeAndTrackPath"/>.
        /// This represents a determined <see cref="Existence"/> of the path, though tracking that existence may have failed
        /// (this is captured in <see cref="PossibleTrackingResult"/>).
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public readonly struct ProbeResult
        {
            /// <summary>
            /// Existence of the probed path (provided even if tracking failed).
            /// </summary>
            public readonly PathExistence Existence;

            /// <summary>
            /// Result of attempting to track existence of the probed path.
            /// </summary>
            public readonly Possible<Unit> PossibleTrackingResult;

            /// <nodoc />
            public ProbeResult(PathExistence existence)
            {
                Existence = existence;
                PossibleTrackingResult = Unit.Void;
            }

            /// <nodoc />
            public ProbeResult(PathExistence existence, Possible<Unit> trackingResult)
            {
                Existence = existence;
                PossibleTrackingResult = trackingResult;
            }
        }

        /// <summary>
        /// Probes for the existence of a path, while also tracking the result (e.g. if a file does not exist and is later created, that change will be detected).
        /// </summary>
        /// <remarks>
        /// We represent two levels of failure in tracking probes:
        /// - Probing failed (existence is unknown).
        /// - Probing succeeded, but tracking failed (existence is known)
        /// - Tracking and probing both succeeded (existence is known)
        /// This is needed since consumers like <see cref="FileChangeTracker"/> need to observe tracking failures gracefully while still using the existence result.
        /// </remarks>
        public Possible<ProbeResult> TryProbeAndTrackPath(
            string path, 
            ExistenceTrackingFilter filter = ExistenceTrackingFilter.TrackAlways,
            bool? isReadOnly = default)
        {
            using (Counters.StartStopwatch(FileChangeTrackingCounter.TryProbeAndTrackPathTime))
            {
                AbsolutePath internalPath = AbsolutePath.Create(m_internalPathTable, path);
                Possible<ProbeResult> probeResult = TryProbeAndTrackPathInternal(internalPath, filter);
                Counters.IncrementCounter(FileChangeTrackingCounter.FileSystemProbeCount);

                return probeResult;
            }
        }

        private Possible<ProbeResult> TryProbeAndTrackPathInternal(AbsolutePath internalPath, ExistenceTrackingFilter filter = ExistenceTrackingFilter.TrackAlways)
        {
            return TryOpenAndTrackPathInternal(internalPath, FileDesiredAccess.FileReadAttributes, filter).Then(
                result =>
                {
                    using (result)
                    {
                        if (!result.Existent)
                        {
                            return new ProbeResult(PathExistence.Nonexistent, result.PossibleTrackingResult.Then(_ => Unit.Void));
                        }
                        else
                        {
                            // For final path existence check, we treat any symlink as file.
                            // We can call FileUtilities.TryProbePathExistence here, but we inline it to avoid opening handle.
                            FileAttributes attributes = FileUtilities.GetFileAttributesByHandle(result.Handle);

                            bool hasSymlinkFlag = (attributes & FileAttributes.ReparsePoint) != 0;
                            bool hasDirectoryFlag = (attributes & FileAttributes.Directory) != 0;

                            PathExistence specificExistence = hasSymlinkFlag 
                                ? PathExistence.ExistsAsFile 
                                : (hasDirectoryFlag ? PathExistence.ExistsAsDirectory : PathExistence.ExistsAsFile);

                            return new ProbeResult(specificExistence, result.PossibleTrackingResult.Then(_ => Unit.Void));
                        }
                    }
                });
        }

        /// <summary>
        /// Result of attempting to open and track a path with <see cref="FileChangeTrackingSet.TryOpenAndTrackPathInternal"/>.
        /// This represents determination of if a path is <see cref="Existent"/> and (if so) the resulting handle, though tracking
        /// the opened file (or its absence) may have failed (see <see cref="PossibleTrackingResult"/>).
        /// This result must be disposed, since it may contain a handle.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        private readonly struct TrackAndOpenResult : IDisposable
        {
            /// <summary>
            /// Indicates if the specified path was existent and actually open (provided even if tracking failed).
            /// </summary>
            public readonly bool Existent;

            private readonly SafeFileHandle m_handle;

            /// <summary>
            /// Result of attempting to track existence of the probed path (or, the fact that it was skipped due to a <see cref="ExistenceTrackingFilter"/>).
            /// </summary>
            public readonly Possible<ConditionalTrackingResult> PossibleTrackingResult;

            /// <nodoc />
            public TrackAndOpenResult(bool existent, Possible<ConditionalTrackingResult> trackingResult, SafeFileHandle handle)
            {
                Contract.Requires(existent == (handle != null));

                Existent = existent;
                PossibleTrackingResult = trackingResult;
                m_handle = handle;
            }

            /// <nodoc />
            public static TrackAndOpenResult CreateNonexistent(Possible<ConditionalTrackingResult> trackingResult)
            {
                return new TrackAndOpenResult(existent: false, trackingResult: trackingResult, handle: null);
            }

            /// <nodoc />
            public static TrackAndOpenResult CreateExistent(Possible<ConditionalTrackingResult> trackingResult, SafeFileHandle handle)
            {
                Contract.Requires(handle != null);
                return new TrackAndOpenResult(existent: true, trackingResult: trackingResult, handle: handle);
            }

            /// <summary>
            /// Opened handle. Only available if <see cref="Existent"/>.
            /// </summary>
            public SafeFileHandle Handle
            {
                get
                {
                    Contract.Requires(Existent);
                    return m_handle;
                }
            }

            /// <summary>
            /// Disposes the opened handle, if present.
            /// </summary>
            public void Dispose()
            {
                m_handle?.Dispose();
            }
        }

        /// <summary>
        /// Combined operation of opening a handle to a path (which may be a directory) and tracking the path.
        /// - If the provided path exists, that file is tracked.
        /// - If some part of the path does not exist, then non-existence is tracked (via tracking additions to the first existent parent).
        /// </summary>
        /// <remarks>
        /// The returned result is disposable since it may contain the opened handle. The caller is responsible for closing that handle.
        /// The <paramref name="filter"/> and <paramref name="existentFileFilter"/> parameter allows conditionally skipping tracking (while still attempting to open / determine existence);
        /// this is useful to express e.g. a 're-track if still absent' operation, as used to prevent false-positives from sibling changes in directories.
        /// </remarks>
        /// <param name="internalPath">Path to open (must be in <see cref="m_internalPathTable"/>)</param>
        /// <param name="access">Requested access to the path (granted on the returned handle, if existent)</param>
        /// <param name="filter">Indicates which circumstances (existent or non-existent) should result in full tracking. See remarks</param>
        /// <param name="existentFileFilter">If the path exists, called with the open handle to determine if tracking should proceed. 'true' indicates normal tracking, and 'false' prevents tracking.</param>
        private Possible<TrackAndOpenResult> TryOpenAndTrackPathInternal(
            AbsolutePath internalPath,
            FileDesiredAccess access,
            ExistenceTrackingFilter filter,
            Func<SafeFileHandle, bool> existentFileFilter = null)
        {
            // We may need multiple attempts to get a consistent snapshot of path absence.
            // For typical-case efficiency we walk up from the deepest path (internalPath) to find the deepest existent path.
            // However, the following sequence can occur:
            // - Fail to open D\E\F and then D\E (nonexistent)
            // - (D\E is then created by some concurrent process, or a rename swap occurs resulting in a different D\)
            // - Open D\ and track it (yields subscription)
            // In order to satisfy the requirement of TrackAbsentChildPath that the subscription is created *before* the failed child probe, we add one more step:
            // - Probe D\E again (exists this time; try again from the beginning)
            // Generally, it is safe to make any observation of a path D\ after a subscription is obtained (but not before).
            bool snapshotConsistent;

            // Existence of internalPath (established on each snapshot attempt).
            bool? pathExistent;

            // Subscription for the first existent path (we assume some volume root directory always exists).
            FileChangeTrackingSubscription subscriptionForExistentPath = FileChangeTrackingSubscription.Invalid;
            PathExistence pathExistence = PathExistence.Nonexistent;

            SafeFileHandle primaryPathHandle = null; // If the primary path (internalPath) is opened and thus existent, then we hold on to it to be eventually returned to the caller.

            do
            {
                snapshotConsistent = true; // Consistent until proven otherwise (failed verification of snapshotVerificationPath).
                AbsolutePath snapshotVerificationPath = AbsolutePath.Invalid; // We only need to verify if we have at least one absent path.
                FileFlagsAndAttributes snapshotVerificationFlagsAndAttributes = FileFlagsAndAttributes.None;
                FileDesiredAccess snapshotVerificationDesiredAccess = FileDesiredAccess.None;

                pathExistent = null; // The first open attempt is for internalPath itself, and that fully determines the probe result.

                // Abandon handle from prior snapshot attempt.
                if (primaryPathHandle != null)
                {
                    primaryPathHandle.Dispose();
                    primaryPathHandle = null;
                }

                for (AbsolutePath currentPath = internalPath; currentPath.IsValid; currentPath = currentPath.GetParent(m_internalPathTable))
                {
                    bool isPrimaryPath = currentPath == internalPath;
                    Contract.Assert(isPrimaryPath == !pathExistent.HasValue, "First iteration assigns pathExistence");

                    bool skipTrackingIfNonexistent = isPrimaryPath && (filter & ExistenceTrackingFilter.TrackIfNonexistent) == 0;
                    bool skipTrackingIfExistent = isPrimaryPath && (filter & ExistenceTrackingFilter.TrackIfExistent) == 0;

                    SafeFileHandle currentPathHandle;
                    string expandedPath = currentPath.ToString(m_internalPathTable);
                    var desiredAccess = isPrimaryPath ? access : FileDesiredAccess.None;

                    FileFlagsAndAttributes openFlags = OpenFlags;

                    OpenFileResult openResult = FileUtilities.TryOpenDirectory(
                        expandedPath,
                        desiredAccess,
                        FileShare.ReadWrite | FileShare.Delete,
                        openFlags,
                        out currentPathHandle);

                    if (isPrimaryPath)
                    {
                        primaryPathHandle = currentPathHandle;
                    }

                    using (isPrimaryPath ? null : currentPathHandle)
                    {
                        if (openResult.Status.IsNonexistent())
                        {
                            if (isPrimaryPath)
                            {
                                pathExistent = false;
                            }
                            else
                            {
                                Contract.Assume(
                                    !pathExistent.Value,
                                    "We only find non-existent parent paths after finding the primary path non-existent");
                            }

                            Contract.Assert(
                                primaryPathHandle == null,
                                "primaryPathHandle is only set on a snapshot attempt where the first (primary) open succeeded.");

                            if (skipTrackingIfNonexistent)
                            {
                                // Non-tracking fast-path: On the first non-existence result, we can return right away without tracking anything.
                                return TrackAndOpenResult.CreateNonexistent(ConditionalTrackingResult.SkippedDueToFilter);
                            }

                            if ((m_internalPathTable.GetContainerAndFlags(currentPath.Value).Item2 & Absent) != 0)
                            {
                                // Tracking fast path: We've done at least one probe that determined non-existence. The path was already marked
                                //            absent, so we don't actually need to keep walking up to find an existent parent.
                                //            This means that repeatedly probed and absent paths eventually cost one (failed) handle open
                                //            rather than many.
                                if (ExtendAbsentPath(currentPath, internalPath))
                                {
                                    return TrackAndOpenResult.CreateNonexistent(ConditionalTrackingResult.Tracked);
                                }
                            }

                            // Slower path: We should eventually find some existent parent path (volume root?)
                            //              When we do, we need to double-check the immediate child that did *not* exist.
                            //              (This establishes an existent path -> absent path edge transition that can later be extended in the fast path case).
                            snapshotVerificationPath = currentPath;
                            snapshotVerificationDesiredAccess = desiredAccess;
                            snapshotVerificationFlagsAndAttributes = openFlags;
                        }
                        else
                        {
                            // Other open failures are unexpected.
                            if (!openResult.Succeeded)
                            {
                                return openResult.CreateFailureForError().Annotate(
                                    "Failed to open a path as part of a change-tracking probe (existence only): " + expandedPath);
                            }

                            if (isPrimaryPath)
                            {
                                // We now have a primaryPathHandle set which must be returned to the caller or disposed.
                                // All following early-returns must mention primaryPathHandle.
                                Contract.Assert(primaryPathHandle != null, "Open succeeded");

                                bool existentFileFilterRequestsSkippingTacking = false;
                                if (existentFileFilter != null)
                                {
                                    existentFileFilterRequestsSkippingTacking = !existentFileFilter(primaryPathHandle);
                                }

                                if (skipTrackingIfExistent || existentFileFilterRequestsSkippingTacking)
                                {
                                    // Non-tracking fast-path: We found an existent primary path, and so by some filter we must skip all tracking below.
                                    return TrackAndOpenResult.CreateExistent(ConditionalTrackingResult.SkippedDueToFilter, primaryPathHandle);
                                }
                                else
                                {
                                    pathExistent = true;
                                }
                            }

                            Contract.Assert(pathExistent.HasValue, "Set on first 'for' iteration");
                            Contract.Assert(currentPathHandle != null, "Open succeeded");

                            var possibleIdentity = VersionedFileIdentity.TryQuery(currentPathHandle);

                            if (!possibleIdentity.Succeeded)
                            {
                                Failure failureToQueryIdentity = possibleIdentity.Failure.Annotate(
                                    "Failed to query the identity of a path as part of a change-tracking open: " + expandedPath);

                                return new TrackAndOpenResult(pathExistent.Value, failureToQueryIdentity, primaryPathHandle);
                            }

                            VersionedFileIdentity identity = possibleIdentity.Result;

                            Possible<FileChangeTrackingSubscription> possiblyTracked = TryTrackChangesToFileInternal(
                                currentPathHandle,
                                currentPath,
                                identity);

                            if (!possiblyTracked.Succeeded)
                            {
                                Failure failureToTrackExistence = possiblyTracked.Failure.Annotate(
                                    "Failed to track a path as part of a change-tracking open: " + expandedPath);

                                return new TrackAndOpenResult(pathExistent.Value, failureToTrackExistence, primaryPathHandle);
                            }

                            // For annotating path table, we essentially follow the symlink for checking existence, so that we can mark 
                            // directory symlink, if a path is a directory symlink, as a possible container in the path table.
                            pathExistence = (FileUtilities.GetFileAttributesByHandle(currentPathHandle) & FileAttributes.Directory) != 0
                                    ? PathExistence.ExistsAsDirectory
                                    : PathExistence.ExistsAsFile;

                            subscriptionForExistentPath = possiblyTracked.Result;

                            if (snapshotVerificationPath.IsValid)
                            {
                                string expandedVerificationPath = snapshotVerificationPath.ToString(m_internalPathTable);
                                if (
                                    !CheckPathStillNonexistent(
                                        expandedVerificationPath: expandedVerificationPath,
                                        expandedFullPath: expandedPath,
                                        desiredAccess: snapshotVerificationDesiredAccess,
                                        openFlags: snapshotVerificationFlagsAndAttributes))
                                {
                                    // Will break below and then start over due to while (!snapshotConsistent)
                                    snapshotConsistent = false;
                                }
                            }

                            break; // We found an existent path, so are done with this snapshot attempt (maybe snapshotConsistent is set also)
                        }
                    }
                }
            }
            while (!snapshotConsistent);

            Contract.Assert(filter != ExistenceTrackingFilter.TrackNever, "If tracking is always skipped, we should have returned already (existence or not already established)");
            Contract.Assume(pathExistent.HasValue, "internalPath is the first path visited on each snapshot attempt, and on that visit we determine its existence");
            Contract.Assume(pathExistent.Value == (primaryPathHandle != null), "pathExistent implies we opened the primary path successfully");

            if (subscriptionForExistentPath.IsValid)
            {
                if (subscriptionForExistentPath.ChangeTrackingSetInternalPath != internalPath)
                {
                    TrackAbsentChildPath(subscriptionForExistentPath, pathExistence, internalPath);
                }
            }
            else
            {
                // Subscription is invalid, which means that we reached a volume root that does not exist.
                // This scenario is possible because Detours can report probing of non-existent paths (see SandboxConfiguration.IgnoreNonExistentProbes),
                // and tools can probe whatever paths they like.
                TrackAbsentPath(internalPath);
            }

            return new TrackAndOpenResult(pathExistent.Value, ConditionalTrackingResult.Tracked, primaryPathHandle);
        }

        private bool CheckPathStillNonexistent(
            string expandedVerificationPath,
            string expandedFullPath,
            FileDesiredAccess desiredAccess,
            FileFlagsAndAttributes openFlags)
        {
            SafeFileHandle verificationHandle;
            OpenFileResult verificationOpenResult = FileUtilities.TryOpenDirectory(
                expandedVerificationPath,
                desiredAccess,
                FileShare.ReadWrite | FileShare.Delete,
                openFlags,
                out verificationHandle);

            using (verificationHandle)
            {
                if (!verificationOpenResult.Status.IsNonexistent())
                {
                    Logger.Log.ChangeDetectionProbeSnapshotInconsistent(
                        m_loggingContext,
                        expandedVerificationPath,
                        expandedFullPath,
                        verificationOpenResult.NativeErrorCode);
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Saves this change tracking set so that it can be reloaded. Note that change tracking sets are machine-specific
        /// and so should not be shared among machines.
        /// </summary>
        /// <remarks>
        /// If the tracker is disabled, only save a boolean which indicates that the tracker is disabled in the prior build.
        /// If processing a record causes a problem, we do not need to process the same record again in the future runs.
        /// In the next run, a tracker will be created in the <see cref="FileChangeTrackingState.BuildingInitialChangeTrackingSet"/>
        /// when isDisabledTracker is read as true.
        /// </remarks>
        public void Save(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            m_internalPathTable.StringTable.Serialize(writer);
            m_internalPathTable.Serialize(writer);
            int numberOfMembershipEntries = m_directoryMembershipTrackingFingerprints.Count;
            Contract.Assume(numberOfMembershipEntries >= 0);

            writer.Write(numberOfMembershipEntries);

            int membershipEntriesWritten = 0;
            foreach (KeyValuePair<AbsolutePath, DirectoryMembershipTrackingFingerprint> membershipEntry in m_directoryMembershipTrackingFingerprints)
            {
                writer.Write(membershipEntry.Key);
                membershipEntry.Value.Hash.SerializeHashBytes(writer);
                membershipEntriesWritten++;
            }

            Contract.Assume(membershipEntriesWritten == numberOfMembershipEntries);

            VolumeMap.Serialize(writer);

            var nonEmptyVolumes = new List<ulong>();
            foreach (KeyValuePair<ulong, SingleVolumeFileChangeTrackingSet> volumeSerialAndTrackingSet in m_perVolumeChangeTrackingSets)
            {
                if (volumeSerialAndTrackingSet.Value.IsEmpty)
                {
                    // If there are no tracked files for a volume, it is not needed to save the change tracking set for that volume.
                    continue;
                }

                nonEmptyVolumes.Add(volumeSerialAndTrackingSet.Key);
            }

            writer.Write(nonEmptyVolumes.Count);
            foreach (var volumeSerial in nonEmptyVolumes)
            {
                var volumeTrackingSet = m_perVolumeChangeTrackingSets[volumeSerial];
                writer.Write(volumeSerial);
                writer.Write(volumeTrackingSet.CurrentCheckpoint.JournalId);
                writer.Write(volumeTrackingSet.CurrentCheckpoint.CheckpointUsn.Value);
                writer.Write(volumeTrackingSet.TrackedFilesCount);
            }

            foreach (var volumeSerial in nonEmptyVolumes)
            {
                var volumeTrackingSet = m_perVolumeChangeTrackingSets[volumeSerial];
                volumeTrackingSet.SaveTrackedFiles(writer);
            }
        }

        /// <summary>
        /// Loads a persisted change tracking set. Note that change tracking sets are machine-specific
        /// and so should not be shared among machines. The load will fail if the provided atomic save token does
        /// not match the one stored in the persisted change tracking set; this allows referencing
        /// a change tracking set in some secondary safely (only if it corresponds to other atomically-saved state). See class remarks.
        /// </summary>
        public static LoadingTrackerResult TryLoad(
            LoggingContext loggingContext,
            FileEnvelopeId fileEnvelopeId,
            BuildXLReader reader,
            VolumeMap volumeMap,
            IChangeJournalAccessor journal,
            Stopwatch stopwatch,
            bool createForAllCapableVolumes = true)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(reader != null);
            Contract.Requires(!createForAllCapableVolumes || volumeMap != null);
            Contract.Requires(!createForAllCapableVolumes || journal != null);

            StringTable internalStringTable = StringTable.DeserializeAsync(reader).GetAwaiter().GetResult();
            PathTable internalPathTable = PathTable.DeserializeAsync(reader, Task.FromResult(internalStringTable)).GetAwaiter().GetResult();

            var membershipFingerprints = new ConcurrentDictionary<AbsolutePath, DirectoryMembershipTrackingFingerprint>();
            {
                int numberOfMembershipEntries = reader.ReadInt32();
                if (numberOfMembershipEntries < 0)
                {
                    throw new BuildXLException("Invalid number of directory membership entries in serialized change tracking state");
                }

                for (int i = 0; i < numberOfMembershipEntries; i++)
                {
                    AbsolutePath path = reader.ReadAbsolutePath();
                    DirectoryMembershipTrackingFingerprint fingerprint = new DirectoryMembershipTrackingFingerprint(
                        ContentHashingUtilities.CreateFrom(reader));

                    bool added = membershipFingerprints.TryAdd(path, fingerprint);
                    Contract.Assume(added, "Unexpected duplicate key");
                }
            }

            var readVolumeMap = VolumeMap.Deserialize(reader);
            volumeMap?.ValidateJunctionRoots(loggingContext, readVolumeMap);

            int trackedVolumesCount = reader.ReadInt32();
            Contract.Assume(trackedVolumesCount >= 0);

            // List of Volume Serial and Journal Id
            var trackedVolumeSerials = new List<ulong>();
            var knownJournalIds = new Dictionary<ulong, ulong>();
            var knownCheckpoints = new Dictionary<ulong, Usn>();
            int trackedFilesCount = 0;
            for (int i = 0; i < trackedVolumesCount; i++)
            {
                ulong volumeSerial = reader.ReadUInt64();
                ulong journalId = reader.ReadUInt64();
                Usn volumeCheckpoint = new Usn(reader.ReadUInt64());
                trackedVolumeSerials.Add(volumeSerial);
                knownJournalIds.Add(volumeSerial, journalId);
                knownCheckpoints.Add(volumeSerial, volumeCheckpoint);
                trackedFilesCount += reader.ReadInt32();
            }

            ulong trackedJournalsSizeBytes = 0;
            Dictionary<ulong, Usn> nextUsns = default;
            FileChangeTrackingSet trackingSet;

            if (createForAllCapableVolumes)
            {
                trackingSet = CreateForAllCapableVolumesFromPriorCheckpoint(
                    loggingContext,
                    volumeMap,
                    journal,
                    internalPathTable: internalPathTable,
                    membershipFingerprints: membershipFingerprints,
                    hasNewFileOrCheckpointData: true,
                    knownCheckpoints: knownCheckpoints,
                    trackedJournalsSizeBytes: out trackedJournalsSizeBytes,
                    nextUsns: out nextUsns);
            }
            else
            {
                volumeMap = readVolumeMap;
                trackingSet = CreateForKnownVolumesFromPriorCheckpoint(
                    loggingContext,
                    volumeMap,
                    hasNewFileOrCheckpointData: true,
                    internalPathTable: internalPathTable,
                    membershipFingerprints: membershipFingerprints,
                    knownJournalIds: knownJournalIds,
                    knownCheckpoints: knownCheckpoints);
            }

            foreach (var knownVolumeSerial in trackedVolumeSerials)
            {
                if (!trackingSet.m_perVolumeChangeTrackingSets.TryGetValue(
                    knownVolumeSerial,
                    out SingleVolumeFileChangeTrackingSet volumeTrackingSet))
                {
                    // A non-empty tracking set was recorded in prior build, but now its volume
                    // serial is missing.
                    return LoadingTrackerResult.FailMissingVolumeJournal(knownVolumeSerial);
                }

                ulong knownJournalId = knownJournalIds[knownVolumeSerial];

                if (knownJournalId != volumeTrackingSet.CurrentCheckpoint.JournalId)
                {
                    return LoadingTrackerResult.FailJournalIdMismatch(knownVolumeSerial);
                }

                if (nextUsns != null 
                    && nextUsns.TryGetValue(knownVolumeSerial, out Usn nextUsn)
                    && nextUsn < volumeTrackingSet.CurrentCheckpoint.CheckpointUsn)
                {
                    return LoadingTrackerResult.FailJournalGoesBackInTime(
                        knownVolumeSerial, 
                        nextUsn, 
                        volumeTrackingSet.CurrentCheckpoint.CheckpointUsn);
                }

                volumeTrackingSet.LoadTrackedFiles(reader);
            }

            return LoadingTrackerResult.Success(
                fileEnvelopeId,
                trackingSet,
                trackedVolumesCount,
                trackedJournalsSizeBytes,
                stopwatch.ElapsedMilliseconds);
        }

        #endregion

        #region Text writing

        /// <summary>
        /// Writes textual format of <see cref="FileChangeTrackingSet"/>.
        /// </summary>
        public void WriteText(TextWriter writer)
        {
            Contract.Requires(writer != null);

            foreach (var singleVolumeFileChangeTrackingSet in m_perVolumeChangeTrackingSets)
            {
                singleVolumeFileChangeTrackingSet.Value.WriteText(writer);
                writer.WriteLine(string.Empty);
            }

            writer.WriteLine(string.Empty);
            writer.WriteLine("Paths tracked as absent:");
            for (int i = 1; i < m_internalPathTable.Count; ++i)
            {
                var path = new AbsolutePath(i);
                if ((m_internalPathTable.GetContainerAndFlags(path.Value).Item2 & Absent) != 0)
                {
                    writer.WriteLine(I($"\t- {path.ToString(m_internalPathTable)}"));
                }
            }

            writer.WriteLine(string.Empty);
            writer.WriteLine("Enumerated directories:");
            foreach (var trackedDirectory in m_directoryMembershipTrackingFingerprints)
            {
                string hashStr = trackedDirectory.Value.Hash.HashType != BuildXL.Cache.ContentStore.Hashing.HashType.Unknown
                    ? trackedDirectory.Value.Hash.ToString()
                    : nameof(BuildXL.Cache.ContentStore.Hashing.HashType.Unknown);

                writer.WriteLine(I($"\t- {trackedDirectory.Key.ToString(m_internalPathTable)}: {hashStr}"));
            }
        }

        #endregion

        /// <summary>
        /// Checkpoint with journal ID.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public readonly struct CheckPointWithJournalId
        {
            /// <summary>
            /// USN checkpoint.
            /// </summary>
            public readonly Usn CheckpointUsn;

            /// <summary>
            /// Journal ID.
            /// </summary>
            public readonly ulong JournalId;

            /// <summary>
            /// Creates an instance of <see cref="CheckPointWithJournalId"/>.
            /// </summary>
            public CheckPointWithJournalId(Usn checkpointUsn, ulong journalId)
            {
                CheckpointUsn = checkpointUsn;
                JournalId = journalId;
            }
        }

        #region Observable

        /// <inheritdoc />
        public IDisposable Subscribe(IObserver<ChangedPathInfo> observer)
        {
            Contract.Requires(observer != null);

            if (!m_observers.Contains(observer))
            {
                m_observers.Add(observer);
            }

            return new FileChangeTrackingSetUnsubscriber<ChangedPathInfo>(m_observers, observer);
        }

        private void ReportChangedPath(ChangedPathInfo changedPathInfo)
        {
            foreach (var observer in m_observers)
            {
                observer.OnNext(changedPathInfo);
            }
        }

        private void ReportChangedPathError(Exception exception)
        {
            foreach (var observer in m_observers)
            {
                observer.OnError(exception);
            }
        }

        private void ReportChangedPathCompletion()
        {
            foreach (var observer in m_observers)
            {
                observer.OnCompleted();
            }
        }

        /// <inheritdoc />
        public IDisposable Subscribe(IObserver<ChangedFileIdInfo> observer)
        {
            Contract.Requires(observer != null);

            return new MultiVolumesChangeTrackingUnsubscriber<ChangedFileIdInfo>(
                observer,
                TrackedVolumes.Select(
                    serial => new KeyValuePair<ulong, IDisposable>(serial, m_perVolumeChangeTrackingSets[serial].Subscribe(observer))).ToList());
        }

        #endregion

        /// <summary>
        /// Represents a set of files with version information for which changes should be tracked.
        /// This set may track files on a single volume.
        /// </summary>
        public sealed class SingleVolumeFileChangeTrackingSet : IObservable<ChangedFileIdInfo>
        {
            /// <summary>
            /// Volume serial of the local volume that this set tracks.
            /// </summary>
            private readonly ulong m_volumeSerialNumber;

            /// <summary>
            /// Generation number for this volume's journal (required for robustness against deletion and recreation).
            /// </summary>
            private readonly ulong m_journalId;

            /// <summary>
            /// Cursor indicating the location in the volume's change journal from which new changes would be read.
            /// All change records preceding this checkpoint have already been processed.
            /// </summary>
            private Usn m_checkpoint;

            private readonly PathTable m_internalPathTable;

            private readonly ConcurrentDictionary<FileId, FileChangeTrackingRecord> m_recordsByFileId =
                new ConcurrentDictionary<FileId, FileChangeTrackingRecord>();

            /// <summary>
            /// When re-tracking an already-tracked file, the caller may indicate that existing tracking should be superseded.
            /// Rather than cleaning up all superseded tracking records, we establish a USN lower-bound for the path so that
            /// the superseded records can be ignored on the next scan.
            /// </summary>
            private readonly ConcurrentDictionary<AbsolutePath, Usn> m_pathSupersessionLimits =
                new ConcurrentDictionary<AbsolutePath, Usn>();

            private readonly LoggingContext m_loggingContext;

            private readonly List<IObserver<ChangedFileIdInfo>> m_observers = new List<IObserver<ChangedFileIdInfo>>();

            /// <summary>
            /// <see cref="FileChangeTrackingSet"/> that owns this instance of <see cref="SingleVolumeFileChangeTrackingSet"/>.
            /// </summary>
            public FileChangeTrackingSet OwningFileChangeTrackingSet { get; set; }

            /// <summary>
            /// Creates an instance of <see cref="SingleVolumeFileChangeTrackingSet"/>.
            /// </summary>
            public SingleVolumeFileChangeTrackingSet(
                LoggingContext loggingContext,
                PathTable pathTable,
                VolumeGuidPath volumeGuidPath,
                ulong volumeSerialNumber,
                ulong journalId,
                Usn initialCheckpoint,
                ulong journalSizeInBytes)
            {
                Contract.Requires(loggingContext != null);
                Contract.Requires(pathTable != null);

                m_loggingContext = loggingContext;
                m_internalPathTable = pathTable;
                VolumeGuidPath = volumeGuidPath;
                m_volumeSerialNumber = volumeSerialNumber;
                m_journalId = journalId;
                m_checkpoint = initialCheckpoint;
                JournalSizeInBytes = journalSizeInBytes;
            }

            /// <summary>
            /// Journal size in bytes
            /// </summary>
            public ulong JournalSizeInBytes { get; }

            /// <summary>
            /// Volume guid path of the local volume that this set tracks.
            /// </summary>
            public VolumeGuidPath VolumeGuidPath { get; }

            /// <summary>
            /// Indicates if this change tracking set is empty. Changes may not be read for an empty set.
            /// </summary>
            public bool IsEmpty => m_recordsByFileId.Count == 0;

            /// <summary>
            /// How many files are tracked
            /// </summary>
            public int TrackedFilesCount => m_recordsByFileId.Count;

            /// <summary>
            /// Most recent checkpoint with journal id.
            /// </summary>
            internal CheckPointWithJournalId CurrentCheckpoint => new CheckPointWithJournalId(m_checkpoint, m_journalId);

            #region Tracking files - thread safe, but not allowed concurrently with detecting and processing changes

            /// <summary>
            /// Attempts to add the given change tracking information to the change tracking set.
            /// The file's current version may be later than the current checkpoint of the change tracking set;
            /// if so, changes between the checkpoint and the current version of the file are potentially (and ideally) excluded.
            /// If the file was already added at an earlier version, the tracked version is updated and the prior discarded.
            /// </summary>
            /// <remarks>
            /// Note that this function assumes that the given file belongs to this tracking set's volume.
            /// This is enforced by <see cref="FileChangeTrackingSet"/> dispatching based on volume serial.
            /// </remarks>
            public Possible<FileChangeTrackingSubscription> TryTrackChangesToFile(
                TrackingUpdateMode updateMode,
                SafeFileHandle handle,
                AbsolutePath path,
                VersionedFileIdentity identity)
            {
                return TryTrackChangesToPath(updateMode, handle, path, identity, false);
            }

            private Possible<FileChangeTrackingSubscription> TryTrackChangesToParentPath(
                SafeFileHandle handle,
                AbsolutePath path,
                VersionedFileIdentity identity)
            {
                return TryTrackChangesToPath(TrackingUpdateMode.Preserve, handle, path, identity, true);
            }

            private Possible<FileChangeTrackingSubscription> TryTrackChangesToPath(
               TrackingUpdateMode updateMode,
               SafeFileHandle handle,
               AbsolutePath path,
               VersionedFileIdentity identity,
               bool isPathContainer)
            {
                Contract.Requires(handle != null);
                Contract.Requires(path.IsValid);

                // This method requires an open handle due to a locking side-effect on the file system (prevents renames of parent directories).
                Analysis.IgnoreArgument(handle);

                HierarchicalNameId currentPathHierarchicalNameId = path.Value;
                bool added = false;

                while (currentPathHierarchicalNameId.IsValid)
                {
                    var currentPath = new AbsolutePath(currentPathHierarchicalNameId);

                    (HierarchicalNameId nameId, HierarchicalNameTable.NameFlags flags) containerOfCurrentPathAndFlagsOfCurrentPath =
                        m_internalPathTable.GetContainerAndFlags(currentPathHierarchicalNameId);

                    bool isPrimaryPath = currentPathHierarchicalNameId == path.Value;
                    isPathContainer = !isPrimaryPath || isPathContainer;

                    if ((containerOfCurrentPathAndFlagsOfCurrentPath.Item2 & Tracked) != 0)
                    {
                        bool superseding = !isPathContainer && updateMode == TrackingUpdateMode.Supersede;

                        // Note that the intent of superseding is only relevant for a file that is already tracked (or there is nothing to supersede).
                        if (superseding)
                        {
                            Contract.Assert(currentPathHierarchicalNameId == path.Value);

                            // The originally-tracked version of the file may have been replaced with a new one. Since we will ignore all existing
                            // records (if they have a Usn < identity.Usn), we need to ensure that there is a record for the superseding file.
                            // So, despite adding a filter on Usn (m_pathSupersessionLimits), we maintain the invariant that Tracked <=> 'can be invalidated'.
                            AddRecordForFile(identity.FileId, identity.Usn, path);
                            added = true;

                            m_pathSupersessionLimits.AddOrUpdate(
                                path,
                                addValue: identity.Usn,
                                updateValueFactory: (p, existingSupersessionUsn) => new Usn(Math.Max(identity.Usn.Value, existingSupersessionUsn.Value)));
                        }

                        // This path (and therefore all parent paths, by construction) have already been tracked.
                        // See class remarks for why this is sufficient.
                        break;
                    }

                    VersionedFileIdentity currentPathIdentity;

                    if (isPrimaryPath)
                    {
                        currentPathIdentity = identity;
                        Contract.Assert(currentPathIdentity.VolumeSerialNumber == m_volumeSerialNumber);
                    }
                    else
                    {
                        SafeFileHandle currentPathHandle;
                        string expandedPath = currentPath.ToString(m_internalPathTable);

                        OpenFileResult directoryOpenResult = FileUtilities.TryOpenDirectory(
                            expandedPath,
                            FileDesiredAccess.None,
                            FileShare.ReadWrite | FileShare.Delete,
                            OpenFlags,
                            out currentPathHandle);

                        if (!directoryOpenResult.Succeeded)
                        {
                            return directoryOpenResult.CreateFailureForError().Annotate(I($"Failed to open a parent directory '{expandedPath}' for change-tracking"));
                        }

                        using (currentPathHandle)
                        {
                            Possible<VersionedFileIdentity, Failure<VersionedFileIdentity.IdentityUnavailabilityReason>> possibleIdentity =
                                VersionedFileIdentity.TryQuery(currentPathHandle);

                            if (!possibleIdentity.Succeeded)
                            {
                                return possibleIdentity.Failure.Annotate(I($"Failed to query the identity of a parent directory '{expandedPath}' for change-tracking"));
                            }

                            currentPathIdentity = possibleIdentity.Result;

                            if (currentPathIdentity.VolumeSerialNumber != m_volumeSerialNumber)
                            {
                                Contract.Assert(OwningFileChangeTrackingSet != null);

                                if (!OwningFileChangeTrackingSet.m_perVolumeChangeTrackingSets.TryGetValue(
                                    currentPathIdentity.VolumeSerialNumber, 
                                    out SingleVolumeFileChangeTrackingSet otherVolumeTrackingSet))
                                {
                                    return new Failure<string>(I($"Parent path '{expandedPath}' cannot be tracked because it is on a different volume '{currentPathIdentity.VolumeSerialNumber:X16}', and the volume is not in the tracking set"));
                                }

                                Contract.Assert(otherVolumeTrackingSet != null);

                                Possible<FileChangeTrackingSubscription> maybetrackingParent = otherVolumeTrackingSet.TryTrackChangesToParentPath(currentPathHandle, currentPath, currentPathIdentity);

                                if (!maybetrackingParent.Succeeded)
                                {
                                    return maybetrackingParent.Failure;
                                }

                                return new FileChangeTrackingSubscription(path);
                            }
                        }
                    }

                    AddRecordForFile(currentPathIdentity.FileId, currentPathIdentity.Usn, currentPath);
                    added = !isPathContainer ? true : added;

                    // We've succeeded in adding a file record sufficient to invalidate this path later. Marking the path notes this, so that we don't
                    // bother opening a handle to the same path later. See the check on containerOfCurrentPathAndFlagsOfCurrentPath at the top of the loop.
                    // In the event this is a parent path of the path we are directly tracking, ensure that that parent path is marked as a 'container' and so
                    // may not be superseded (we would orphan its children).
                    m_internalPathTable.SetFlags(currentPathHierarchicalNameId, Tracked | (isPathContainer ? Container : HierarchicalNameTable.NameFlags.None));

                    currentPathHierarchicalNameId = containerOfCurrentPathAndFlagsOfCurrentPath.nameId;
                }

                if (added && ETWLogger.Log.IsEnabled(BuildXL.Tracing.Diagnostics.EventLevel.Verbose, Keywords.Diagnostics))
                {
                    Logger.Log.TrackChangesToFileDiagnostic(
                        m_loggingContext,
                        path.ToString(m_internalPathTable),
                        identity.ToString(),
                        updateMode == TrackingUpdateMode.Preserve ? "Preserve" : "Supersede");
                }

                return new FileChangeTrackingSubscription(path);
            }

            private void AddRecordForFile(FileId fileId, Usn usn, AbsolutePath path)
            {
                while (true)
                {
                    FileChangeTrackingRecord existingHeadRecord;
                    if (m_recordsByFileId.TryGetValue(fileId, out existingHeadRecord))
                    {
                        if (existingHeadRecord.Usn == usn && existingHeadRecord.Path == path)
                        {
                            // Most retracks have the same path and usn, particularly from previous build run.
                            // If USN changed, typically the mapping is poped during journal scan.
                            // This check can prevent this tracker from getting bloated over time, particularly when Supersede
                            // tracking mode is used.
                            break;
                        }

                        var newRecord = new FileChangeTrackingRecord(
                            usn,
                            path,
                            next: existingHeadRecord);

                        if (m_recordsByFileId.TryUpdate(fileId, newRecord, comparisonValue: existingHeadRecord))
                        {
                            // Record fully committed.
                            break;
                        }
                        else
                        {
                            // Update conflict (new head record).
                        }
                    }
                    else
                    {
                        var newRecord = new FileChangeTrackingRecord(
                            usn,
                            path,
                            next: null);

                        if (m_recordsByFileId.TryAdd(fileId, newRecord))
                        {
                            // Record fully committed.
                            break;
                        }
                        else
                        {
                            // Update conflict (new head record).
                        }
                    }
                }
            }

            #endregion

            #region Change processing - exclusive access assumed (don't concurrently track more files)

            private void PopImpactedRecordsIfPresent(ref UsnRecord record, LinkImpact impact, List<FileChangeTrackingRecord> targetList)
            {
                Contract.Requires(impact != LinkImpact.None);

                // Untracked files are not relevant.
                FileChangeTrackingRecord existingRecordListHead;
                if (!m_recordsByFileId.TryGetValue(record.FileId, out existingRecordListHead))
                {
                    return;
                }

                Contract.Assume(existingRecordListHead != null);

                // A change record may indicate that a 'file' changed (e.g. data change) or a particular link to a file changed
                // (e.g. deleting a hardlink). The 'SingleLink' case is just like the 'AllLink' case but we exclude invalidating
                // paths that aren't under the parent path as indicated by the change record.
                AbsolutePath parentFilter;
                if (impact == LinkImpact.AllLinks)
                {
                    parentFilter = AbsolutePath.Invalid;
                }
                else if (impact == LinkImpact.SingleLink)
                {
                    FileChangeTrackingRecord parentRecordListHead;

                    // Trivially, we don't care about links for which we didn't track the parent.
                    // (we might track link A\B of a file but not C\D, in which case we may never track directory C).
                    if (!m_recordsByFileId.TryGetValue(record.ContainerFileId, out parentRecordListHead))
                    {
                        return;
                    }

                    Contract.Assume(parentRecordListHead != null);

                    // We at some point tracked the parent directory. We may have it tracked under multiple paths:
                    // - CreateSourceFile D\, D\F, D\G
                    // - Track D\F
                    // - Rename D -> D2
                    // - Track D2\
                    // Rather than generalize parentFilter to a set (of parent paths) for that silly case, instead we
                    // just fall back to not having a filter (like AllLinks handling) if there are multiple paths.
                    FileChangeTrackingRecord currentParentRecord = parentRecordListHead;
                    parentFilter = currentParentRecord.Path;

                    do
                    {
                        if (parentFilter != currentParentRecord.Path)
                        {
                            parentFilter = AbsolutePath.Invalid;
                            break;
                        }
                    }
                    while ((currentParentRecord = currentParentRecord.Next) != null);
                }
                else
                {
                    throw Contract.AssertFailure("Unhandled LinkImpact");
                }

                // We now have a parentFilter set up, if applicable. Now, we are looking at the changed-file record list
                // (existingRecordListHead) and can determine which subset of those records are 'impacted'.
                // - An impacted tracking record must have Usn before that of the current journal record.
                //   (consider the sequence CreateSourceFile F; Write F; Track F - we want to exclude changes that happened before tracking started).
                // - If we are trying to precisely invalidate links, then we should not invalidate records under unrelated parents
                //   (given links A\B and C\D to the same file and a rename A\B -> A\B2, we should be able to invalidate A\B and not C\D).
                FileChangeTrackingRecord newRecordListHead = null;
                FileChangeTrackingRecord preceding = null;
                FileChangeTrackingRecord current = existingRecordListHead;
                while (current != null)
                {
                    bool shouldRemove =
                        current.Usn < record.Usn &&
                        (!parentFilter.IsValid || current.Path.GetParent(m_internalPathTable) == parentFilter);

                    if (shouldRemove)
                    {
                        targetList.Add(current);

                        // Records to remove need to be unlinked. If this is the current head record, we don't have
                        // a 'next' field to update; instead note we have newRecordListHead == null and won't assign
                        // the current head as the new head (see dictionary update cases below).
                        if (preceding != null)
                        {
                            preceding.Next = current.Next;
                        }
                        else
                        {
                            Contract.Assume(newRecordListHead == null);
                        }
                    }
                    else if (newRecordListHead == null)
                    {
                        // The first record not removed is the new head.
                        newRecordListHead = current;
                    }

                    preceding = current;
                    current = current.Next;
                }

                if (newRecordListHead == null)
                {
                    FileChangeTrackingRecord removedHead;
                    bool headWasRemoved = m_recordsByFileId.TryRemove(record.FileId, out removedHead);
                    Contract.Assume(headWasRemoved, "PopImpactedRecordIfPresent should run with exclusive access to tracking records");
                    Contract.Assume(removedHead == existingRecordListHead);
                }
                else if (newRecordListHead != existingRecordListHead)
                {
                    bool updated = m_recordsByFileId.TryUpdate(record.FileId, newRecordListHead, comparisonValue: existingRecordListHead);
                    Contract.Assume(updated, "PopImpactedRecordIfPresent should run with exclusive access to tracking records");
                }
            }

            private void GetPathsStillTrackedInFileIdRecords(FileId fileId, HashSet<AbsolutePath> targetList)
            {
                FileChangeTrackingRecord recordListHead;
                if (!m_recordsByFileId.TryGetValue(fileId, out recordListHead))
                {
                    return;
                }

                Contract.Assume(recordListHead != null);

                FileChangeTrackingRecord currentRecord = recordListHead;

                do
                {
                    targetList.Add(currentRecord.Path);
                }
                while ((currentRecord = currentRecord.Next) != null);
            }

            /// <summary>
            /// Attempts to read the associated volume's change journal from the current checkpoint.
            /// Relevant records are processed with <paramref name="handleRelevantRecord"/>
            /// as they are encountered.
            /// </summary>
            [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
            public MaybeResponse<ReadJournalResponse> ReadRelevantChangesSinceCheckpoint(
                IChangeJournalAccessor journalAccessor,
                IReadOnlyList<AbsolutePath> unchangedJunctionRoots,
                TimeSpan? timeLimitForJournalScanning,
                CounterCollection<ReadJournalCounter> stats,
                Action<PathChanges, AbsolutePath, UsnRecord> handleRelevantRecord)
            {
                if (IsEmpty)
                {
                    // We do not save empty change tracking sets so it is fine if we never advance the checkpoint.
                    return
                        new MaybeResponse<ReadJournalResponse>(
                            new ReadJournalResponse(status: ReadUsnJournalStatus.Success, nextUsn: m_checkpoint));
                }

                int numberOfUsnRecordsProcessed = 0;
                int numberOfUsnRecordsRelevant = 0;
                int numberOfLinkImpactRecords = 0;
                int numberOfMembershipImpactRecords = 0;
                int numberOfPathsInvalidated = 0;
                int numberOfExistentialChanges = 0;
                var handleRelevantChangesDueToLinkImpactStopwatch = new StopwatchVar();
                var handleRelevantChangesDueToMembershipImpactStopwatch = new StopwatchVar();

                var impactedRecordBuffer = new List<FileChangeTrackingRecord>();
                var impactedPathBuffer = new HashSet<AbsolutePath>();
                var impactedPathDueToRenameQueue = new Queue<HierarchicalNameId>();
                var impactedPathsDueToRename = new HashSet<HierarchicalNameId>();

                var containerPathBuffer = new HashSet<AbsolutePath>();
                var impactedContainerQueue = new Queue<HierarchicalNameId>();
                var visitedContainers = new HashSet<AbsolutePath>();

                // We remember all relevent file ids due to the following reasons:
                // 1. They can be removed from m_recordsByFileId once they are processed.
                // 2. We need to report their latest USNs for file content table. 
                var relevantFileIds = new HashSet<FileId>();

                var queryResult = journalAccessor.QueryJournal(new QueryJournalRequest(VolumeGuidPath));
                var readRequest = new ReadJournalRequest(
                    VolumeGuidPath,
                    m_journalId,
                    m_checkpoint,
                    endUsn: queryResult.IsError
                        ? null
                        : (queryResult.Response.Succeeded
                            ? queryResult.Response.Data.NextUsn
                            : (Usn?)null),
                    extraReadCount: null,
                    timeLimit: timeLimitForJournalScanning);

                MaybeResponse<ReadJournalResponse> readResponse = journalAccessor.ReadJournal(
                    readRequest,
                    usnRecord =>
                    {
                        numberOfUsnRecordsProcessed++;
                        bool relevant = false;

                        LinkImpact linkImpact = usnRecord.Reason.LinkImpact();
                        MembershipImpact membershipImpact = usnRecord.Reason.MembershipImpact();

                        // Direct (link) impact: A tracked and existent file-link or directory may have been data-changed, deleted, renamed, etc.
                        //                       This can clear the Tracked flag *and all others* (we untrack the path altogether),
                        if (linkImpact != LinkImpact.None)
                        {
                            impactedRecordBuffer.Clear(); // Drop tracking records from prior iteration (already processed).
                            PopImpactedRecordsIfPresent(ref usnRecord, linkImpact, impactedRecordBuffer);

                            if (impactedRecordBuffer.Count > 0)
                            {
                                using (handleRelevantChangesDueToLinkImpactStopwatch.Start())
                                {
                                    numberOfLinkImpactRecords++;
                                    relevant = true;
                                    relevantFileIds.Add(usnRecord.FileId);

                                    var impactedPathChanges = (membershipImpact & MembershipImpact.Deletion) != 0
                                        ? PathChanges.Removed
                                        : PathChanges.DataOrMetadataChanged;

                                    impactedPathBuffer.Clear();

                                    // We may have one or multiple change tracking records impacted by this USN record.
                                    // (Example of the 'multiple' case: Tracking multiple paths to the same file (hardlinked), and the file's data is changed).
                                    foreach (FileChangeTrackingRecord impactedRecord in impactedRecordBuffer)
                                    {
                                        var stringPath = impactedRecord.Path.ToString(m_internalPathTable);
                                        if (!impactedPathBuffer.Add(impactedRecord.Path))
                                        {
                                            continue;
                                        }

                                        if (!EmitAllChangesIfTracked(
                                            handleRelevantRecord,
                                            impactedRecord.Path,
                                            ref usnRecord,
                                            untrackReason: impactedPathChanges,
                                            respectSupersession: true))
                                        {
                                            continue;
                                        }

                                        if (unchangedJunctionRoots.Contains(impactedRecord.Path))
                                        {
                                            Logger.Log.IgnoredRecordsDueToUnchangedJunctionRootCount(m_loggingContext, impactedRecord.Path.ToString(m_internalPathTable));
                                            stats.IncrementCounter(ReadJournalCounter.IgnoredRecordsDueToUnchangedJunctionRootCount);
                                            // Do not invalidate the child paths so skip the next parts.
                                            continue;
                                        }

                                        numberOfPathsInvalidated++;

                                        if (!m_internalPathTable.HasChild(impactedRecord.Path.Value))
                                        {
                                            continue;
                                        }

                                        // Rename (from) is interesting, since we might be renaming a directory; in that case one
                                        // record actually invalidates multiple paths (everything under the directory path has atomically disappeared).
                                        // Note that we don't match to just Rename (from) though: Since un-tracking a path (EmitChangesIfTracked above)
                                        // always untracks all children, we have an invariant that tracked paths are not 'orphaned' under untracked paths.
                                        // So, in addition to a legitimate rename, we might also report paths possibly Removed if a parent directory has an ACL change.
                                        impactedPathDueToRenameQueue.Clear();
                                        impactedPathDueToRenameQueue.Enqueue(impactedRecord.Path.Value);

                                        // Previously, we don't memoize the visitations of rename/delete. Recall that when we track a path A\B\C\D.txt, we also track
                                        // A, A\B, A\B\C, and A\B\C\D.txt. If we delete A, then we will have records about deletion of A, A\B, A\B\C, and A\B\C\D.txt.
                                        // For each record, without memoization, we will visit the subtree multiple times. For example, for deletion record of A,
                                        // we visit A\B, A\B\C, and A\B\C\D.txt. For deletion record of A\B, we visit A\B\C and A\B\C\D.txt. Thus, such changes
                                        // makes the scanning quadratic in terms of the number of paths. Imagine that a user wipes up their object folder but
                                        // are still using the same tracking set, as happened a number of times in Office Word.

                                        // Memoizing the visitation doesn't affect correctness because, once visited, the flags of the descendant that are
                                        // relevant to tracking (Tracked, Absent, Enumerated, Container) are cleared. Thus, the second visitation of the same descendant
                                        // will in principle be no-op.
                                        while (impactedPathDueToRenameQueue.Count > 0)
                                        {
                                            var invalidatedPath = impactedPathDueToRenameQueue.Dequeue();
                                            foreach (var invalidatedChildPath in m_internalPathTable.EnumerateImmediateChildren(invalidatedPath))
                                            {
                                                if (impactedPathsDueToRename.Add(invalidatedChildPath))
                                                {
                                                    // Note that we specify respectSupersession: false here. When invalidating all flags due to a parent change / rename,
                                                    // *all* children must be invalidated since otherwise we can't be sure they aren't orphaned (even if they have a supersession filter).
                                                    if (EmitAllChangesIfAny(
                                                            handleRelevantRecord,
                                                            new AbsolutePath(invalidatedChildPath),
                                                            ref usnRecord,
                                                            untrackReason: PathChanges.Removed,
                                                            respectSupersession: false))
                                                    {
                                                        numberOfPathsInvalidated++;
                                                    }

                                                    if (m_internalPathTable.HasChild(invalidatedChildPath))
                                                    {
                                                        impactedPathDueToRenameQueue.Enqueue(invalidatedChildPath);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // Parent (membership) impact: A directory may have member names added or removed. This can invalidate prior probes and enumerations.
                        if (membershipImpact != MembershipImpact.None)
                        {
                            containerPathBuffer.Clear();
                            GetPathsStillTrackedInFileIdRecords(usnRecord.ContainerFileId, containerPathBuffer);
                            
                            if (containerPathBuffer.Count > 0)
                            {
                                using (handleRelevantChangesDueToMembershipImpactStopwatch.Start())
                                {
                                    // A membership-impacting change affects not-yet-invalidated paths (and child paths) associated with the containing directory.
                                    numberOfMembershipImpactRecords++;
                                    relevant = true;

                                    HandleMembershipImpacts(
                                        ref usnRecord,
                                        containerPathBuffer,
                                        membershipImpact,
                                        ref numberOfExistentialChanges,
                                        handleRelevantRecord,
                                        impactedContainerQueue,
                                        visitedContainers);
                                }
                            }
                        }

                        if (relevant)
                        {
                            numberOfUsnRecordsRelevant++;
                        }

                        if (relevantFileIds.Contains(usnRecord.FileId) || m_recordsByFileId.ContainsKey(usnRecord.FileId))
                        {
                            ReportChangedFileId(
                                new ChangedFileIdInfo(new FileIdAndVolumeId(m_volumeSerialNumber, usnRecord.FileId), usnRecord));
                        }
                    });

                // Counters are not added directly through stats.AddToCounter in ReadJournal due to performance consideration.
                // A number of variables are used as ad-hoc counters, and below, those counters are added to the stats.
                stats.AddToCounter(ReadJournalCounter.RecordsProcessedCount, numberOfUsnRecordsProcessed);
                stats.AddToCounter(ReadJournalCounter.RecordsRelevantCount, numberOfUsnRecordsRelevant);
                stats.AddToCounter(ReadJournalCounter.LinkImpactCount, numberOfLinkImpactRecords);
                stats.AddToCounter(ReadJournalCounter.MembershipImpactCount, numberOfMembershipImpactRecords);
                stats.AddToCounter(ReadJournalCounter.PathsInvalidatedCount, numberOfPathsInvalidated);
                stats.AddToCounter(ReadJournalCounter.ExistentialChangesCount, numberOfExistentialChanges);
                stats.AddToCounter(ReadJournalCounter.HandleRelevantChangesDueToLinkImpactTime, handleRelevantChangesDueToLinkImpactStopwatch.TotalElapsed);
                stats.AddToCounter(ReadJournalCounter.HandleRelevantChangesDueToMembershipImpactTime, handleRelevantChangesDueToMembershipImpactStopwatch.TotalElapsed);

                if (readResponse.IsError)
                {
                    return new MaybeResponse<ReadJournalResponse>(readResponse.Error);
                }

                return
                    new MaybeResponse<ReadJournalResponse>(
                        new ReadJournalResponse(
                            status: readResponse.Response.Status,
                            nextUsn: readResponse.Response.NextUsn,
                            timeout: readResponse.Response.Timeout));
            }

            private void HandleMembershipImpacts(
                ref UsnRecord usnRecord,
                IEnumerable<AbsolutePath> containerPaths,
                MembershipImpact membershipImpact,
                ref int numberOfExistentialChanges,
                Action<PathChanges, AbsolutePath, UsnRecord> handleRelevantRecord,
                Queue<HierarchicalNameId> impactedContainerQueue = null,
                HashSet<AbsolutePath> visitedContainers = null)
            {
                if (impactedContainerQueue == null)
                {
                    impactedContainerQueue = new Queue<HierarchicalNameId>();
                }

                foreach (AbsolutePath containerPath in containerPaths)
                {
                    // Creation / Rename may invalidate 'Absent' flags. We need to visit all child subtrees for which we are tracking absence.
                    // Note that given existent D1\D2, but absent D1\D2\F, an addition to D1 should *not* invalidate D1\D2\F since D2 is not marked absent;
                    // in other words, we don't want to find any absent descendant, but instead any absent subtree rooted inside the container. To this end
                    // we collect those absent containers to visit rather than using EnumerateHierarchyTopDown (which would visit all descendants).
                    if ((membershipImpact & MembershipImpact.Creation) != 0)
                    {
                        if (visitedContainers == null || visitedContainers.Add(containerPath))
                        {
                            impactedContainerQueue.Clear();
                            impactedContainerQueue.Enqueue(containerPath.Value);

                            while (impactedContainerQueue.Count > 0)
                            {
                                HierarchicalNameId currentImpactedContainer = impactedContainerQueue.Dequeue();

                                foreach (
                                    HierarchicalNameId possiblyCreatedChildInImpactedContainer in
                                        m_internalPathTable.EnumerateImmediateChildren(currentImpactedContainer))
                                {
                                    if (m_internalPathTable.SetFlags(possiblyCreatedChildInImpactedContainer, Absent, clear: true))
                                    {
                                        var childPath = new AbsolutePath(possiblyCreatedChildInImpactedContainer);

                                        if (!m_internalPathTable.HasChild(possiblyCreatedChildInImpactedContainer))
                                        {
                                            numberOfExistentialChanges++;
                                            handleRelevantRecord(
                                                PathChanges.NewlyPresent,
                                                childPath,
                                                usnRecord);
                                        }
                                        else if (visitedContainers == null || visitedContainers.Add(childPath))
                                        {
                                            // If childPath is non-existent, then it will be re-tracked by CheckAndMaybeInvalidateAntiDependencies.
                                            handleRelevantRecord(
                                                    PathChanges.NewlyPresent,
                                                    childPath,
                                                    usnRecord);

                                            if (FileUtilities.DirectoryExistsNoFollow(childPath.ToString(m_internalPathTable)))
                                            {
                                                // Only traverse if it's an existing directory. Here, we prune a potentially
                                                // massive path table traversal that is the bottleneck in Office Word.

                                                // Note that we maintain the invariant that if the directory doesn't exist,
                                                // then the descendant paths can keep their Absent marker in the path table.

                                                // This possibly-created child may itself contain more absent paths.
                                                impactedContainerQueue.Enqueue(possiblyCreatedChildInImpactedContainer);
                                                numberOfExistentialChanges++;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Deletion or Creation+Deletion may invalidate enumerated directories (the container itself).
                    if (m_internalPathTable.SetFlags(containerPath.Value, Enumerated, clear: true))
                    {
                        numberOfExistentialChanges++;
                        handleRelevantRecord(PathChanges.MembershipChanged, containerPath, usnRecord);
                    }
                }
            }

            private bool EmitAllChangesIfTracked(
                Action<PathChanges, AbsolutePath, UsnRecord> handle,
                AbsolutePath path,
                ref UsnRecord record,
                PathChanges untrackReason,
                bool respectSupersession)
            {
                return EmitAllChanges(
                    handle, 
                    path, 
                    ref record, 
                    ifAnySet: Tracked, 
                    untrackReason: untrackReason, 
                    respectSupersession: respectSupersession);
            }

            private bool EmitAllChangesIfAny(
                Action<PathChanges, AbsolutePath, UsnRecord> handle,
                AbsolutePath path,
                ref UsnRecord record,
                PathChanges untrackReason,
                bool respectSupersession)
            {
                return EmitAllChanges(
                    handle, 
                    path, 
                    ref record, 
                    ifAnySet: Tracked | Enumerated | Absent, 
                    untrackReason: untrackReason, 
                    respectSupersession: respectSupersession);
            }

            private bool EmitAllChanges(
                Action<PathChanges, AbsolutePath, UsnRecord> handle,
                AbsolutePath path,
                ref UsnRecord record,
                HierarchicalNameTable.NameFlags ifAnySet,
                PathChanges untrackReason,
                bool respectSupersession)
            {
                HierarchicalNameTable.NameFlags currentFlags = m_internalPathTable.GetContainerAndFlags(path.Value).Item2;

                if ((ifAnySet & currentFlags) == 0)
                {
                    return false;
                }

                // ifAnySet filter passed; for non-Container paths, we may additionally filter
                // out changes preceding some supercede-level tracking. For reasoning on why
                // the Container flag prevents this filtering, see remarks for Container.
                if (respectSupersession && ((currentFlags & Container) == 0))
                {
                    Usn supersessionLowerBound;

                    // Note that we do not remove the lower-bound if found. We may encounter many records
                    // below the suppression lower bound for a single file. m_pathSupersessionLimits is cleared
                    // at the end of a scan.
                    if (m_pathSupersessionLimits.TryGetValue(path, out supersessionLowerBound) &&
                        record.Usn < supersessionLowerBound)
                    {
                        return false;
                    }
                }

                // ifAnySet and supersession filters passed. So, *all* potential changes need to be emitted.
                HierarchicalNameTable.NameFlags remainingFlags = currentFlags & (Tracked | Enumerated | Absent | Container);

                var changes = PathChanges.None;

                if ((remainingFlags & Tracked) != 0)
                {
                    changes |= untrackReason;
                }

                if ((remainingFlags & Enumerated) != 0)
                {
                    changes |= PathChanges.MembershipChanged;
                }

                if ((remainingFlags & Absent) != 0)
                {
                    changes |= PathChanges.NewlyPresent;
                }

                m_internalPathTable.SetFlags(path.Value, remainingFlags, clear: true);

                if (changes != PathChanges.None)
                {
                    handle(changes, path, record);
                }

                return true;
            }

            /// <summary>
            /// Updates the checkpoint cursor for this volume. The checkpoint cursor is the USN from which
            /// to begin reading the next time changes are processed.
            /// </summary>
            internal bool CheckpointProcessedChanges(Usn checkpoint)
            {
                Contract.Assume(m_checkpoint <= checkpoint, "Checkpoints should advance");
                bool advancing = m_checkpoint < checkpoint;
                m_checkpoint = checkpoint;

                // Since we read all records up until the present, no supersession lower-bounds can
                // still be relevant (no already-tracked file could have had a USN beyond this checkpoint).
                // Note that we do not clean up supersession entries as they are used, since each may need
                // to suppress multiple change records.
                m_pathSupersessionLimits.Clear();

                return advancing;
            }

            #endregion

            #region Serialization - exclusive access assumed

            internal void LoadTrackedFiles(BuildXLReader reader)
            {
                Contract.Assume(m_recordsByFileId.Count == 0, "Expected to be part of deserialization (into an empty instance)");

                int numberOfFileIdEntries = reader.ReadInt32();
                if (numberOfFileIdEntries < 0)
                {
                    throw new BuildXLException("Invalid number of file ID entries in serialized change tracking state");
                }

                for (int i = 0; i < numberOfFileIdEntries; i++)
                {
                    int numberOfFileIdListEntries = reader.ReadInt32();
                    if (numberOfFileIdListEntries <= 0)
                    {
                        throw new BuildXLException("Invalid number of list entries (for a single file ID( in serialized change tracking state");
                    }

                    var newFileId = FileId.Deserialize(reader);

                    FileChangeTrackingRecord currentHead = null;
                    for (int j = 0; j < numberOfFileIdListEntries; j++)
                    {
                        Usn usn = new Usn(reader.ReadUInt64());
                        AbsolutePath path = reader.ReadAbsolutePath();

                        var newHead = new FileChangeTrackingRecord(usn, path, next: currentHead);
                        currentHead = newHead;
                    }

                    Contract.Assert(currentHead != null);

                    bool added = m_recordsByFileId.TryAdd(newFileId, currentHead);
                    if (!added)
                    {
                        throw new BuildXLException("Duplicate file ID in a serialized change tracking state");
                    }
                }

                Contract.Assume(m_pathSupersessionLimits.Count == 0, "Expected to be part of deserialization (into an empty instance)");

                int numberOfSupersessionEntries = reader.ReadInt32();
                if (numberOfSupersessionEntries < 0)
                {
                    throw new BuildXLException("Invalid number of supersession entries in serialized change tracking state");
                }

                for (int i = 0; i < numberOfSupersessionEntries; i++)
                {
                    AbsolutePath path = reader.ReadAbsolutePath();
                    Usn usn = new Usn(reader.ReadUInt64());

                    bool added = m_pathSupersessionLimits.TryAdd(path, usn);
                    if (!added)
                    {
                        throw new BuildXLException("Duplicate supersession entry in a serialized change tracking state");
                    }
                }
            }

            internal void SaveTrackedFiles(BuildXLWriter writer)
            {
                Contract.Requires(!IsEmpty);
                {
                    int numberOfFileIdEntries = m_recordsByFileId.Count;
                    Contract.Assume(numberOfFileIdEntries >= 0);

                    writer.Write(numberOfFileIdEntries);

                    var recordList = new List<FileChangeTrackingRecord>();

                    // Set used for dedup.
                    var recordSet = new HashSet<(Usn, AbsolutePath)>();

                    int fileIdEntriesWritten = 0;
                    foreach (KeyValuePair<FileId, FileChangeTrackingRecord> fileIdAndListHead in m_recordsByFileId)
                    {
                        Contract.Assume(fileIdEntriesWritten < numberOfFileIdEntries, "Entry was added after serialization started");
                        Contract.Assume(fileIdAndListHead.Value != null);

                        recordSet.Clear();
                        recordList.Clear();
                        {
                            FileChangeTrackingRecord current = fileIdAndListHead.Value;
                            do
                            {
                                if (recordSet.Add((current.Usn, current.Path)))
                                {
                                    recordList.Add(current);
                                }

                                current = current.Next;
                            }
                            while (current != null);
                        }

                        Contract.Assert(recordList.Count > 0);

                        writer.Write(recordList.Count);
                        fileIdAndListHead.Key.Serialize(writer);

                        for (int j = recordList.Count - 1; j >= 0; j--)
                        {
                            FileChangeTrackingRecord current = recordList[j];

                            writer.Write(current.Usn.Value);
                            writer.Write(current.Path);
                        }

                        fileIdEntriesWritten++;
                    }

                    Contract.Assume(fileIdEntriesWritten == numberOfFileIdEntries);
                }

                {
                    int numberOfSupersessionEntries = m_pathSupersessionLimits.Count;
                    Contract.Assume(numberOfSupersessionEntries >= 0);

                    writer.Write(numberOfSupersessionEntries);

                    int supersessionEntriesWritten = 0;
                    foreach (KeyValuePair<AbsolutePath, Usn> supersessionEntry in m_pathSupersessionLimits)
                    {
                        Contract.Assume(supersessionEntriesWritten < numberOfSupersessionEntries, "Entry was added after serialization started");

                        writer.Write(supersessionEntry.Key);
                        writer.Write(supersessionEntry.Value.Value);

                        supersessionEntriesWritten++;
                    }

                    Contract.Assume(supersessionEntriesWritten == numberOfSupersessionEntries);
                }
            }

            #endregion

            #region Logging

            /// <summary>
            /// Logs scanning journal results.
            /// </summary>
            public void LogScanningJournalResult(ScanningJournalResult scanningJournalResult)
            {
                string volumePath = VolumeGuidPath.ToString();

                if (scanningJournalResult.Status == ScanningJournalStatus.JournalEntryDeleted)
                {
                    Logger.Log.ChangeDetectionScanJournalFailedSinceJournalGotOverwritten(
                        m_loggingContext,
                        unchecked((long)m_volumeSerialNumber),
                        volumePath,
                        JournalSizeInBytes.ToString());
                }

                if (scanningJournalResult.Status == ScanningJournalStatus.Timeout)
                {
                    Logger.Log.ChangeDetectionScanJournalFailedSinceTimeout(m_loggingContext, unchecked((long)m_volumeSerialNumber), volumePath);
                }

                var stats = scanningJournalResult.Stats.AsStatistics();
                var statsMessage = Environment.NewLine + string.Join(Environment.NewLine, stats.Select(kvp => "    " + kvp.Key + ": " + kvp.Value));

                Logger.Log.ChangeDetectionSingleVolumeScanJournalResult(
                    m_loggingContext,
                    unchecked((long)m_volumeSerialNumber),
                    volumePath,
                    scanningJournalResult.Status.ToString(),
                    m_checkpoint.ToString(),
                    statsMessage);

                Logger.Log.ChangeDetectionSingleVolumeScanJournalResultTelemetry(
                    m_loggingContext,
                    unchecked((long)m_volumeSerialNumber),
                    volumePath,
                    scanningJournalResult.Status.ToString(),
                    m_checkpoint.ToString(),
                    statsMessage,
                    stats);
            }

            #endregion

            #region Text writing

            /// <summary>
            /// Writes textual format of <see cref="SingleVolumeFileChangeTrackingSet"/>.
            /// </summary>
            public void WriteText(TextWriter writer)
            {
                Contract.Requires(writer != null);

                writer.WriteLine(I($"Volume serial number and path: ({m_volumeSerialNumber:X16} @ {VolumeGuidPath})"));
                writer.WriteLine(I($"\t- Journal id: {m_journalId:X16}"));
                writer.WriteLine(I($"\t- USN checkpoint: {m_checkpoint}"));
                writer.WriteLine("\t- Records by file id:");

                foreach (var fileChangeTrackingRecord in m_recordsByFileId)
                {
                    writer.WriteLine(I($"\t\t* {fileChangeTrackingRecord.Key}"));
                    var current = fileChangeTrackingRecord.Value;

                    while (current != null)
                    {
                        writer.WriteLine(I($"\t\t\t+ {current.Path.ToString(m_internalPathTable)}: {current.Usn}"));
                        current = current.Next;
                    }
                }

                writer.WriteLine("\t- Supersession limit:");

                foreach (var pathSupersessionLimit in m_pathSupersessionLimits)
                {
                    writer.WriteLine(I($"\t\t* {pathSupersessionLimit.Key.ToString(m_internalPathTable)}: {pathSupersessionLimit.Value}"));
                }
            }

            #endregion

            #region Single volume observable

            /// <inheritdoc />
            public IDisposable Subscribe(IObserver<ChangedFileIdInfo> observer)
            {
                Contract.Requires(observer != null);

                if (!m_observers.Contains(observer))
                {
                    m_observers.Add(observer);
                }

                return new FileChangeTrackingSetUnsubscriber<ChangedFileIdInfo>(m_observers, observer);
            }

            private void ReportChangedFileId(ChangedFileIdInfo changedFileIdInfo)
            {
                foreach (var observer in m_observers)
                {
                    observer.OnNext(changedFileIdInfo);
                }
            }

            #endregion Single volume observable
        }
    }

    #region Result wrappers for loading the change tracker and processing journal records

    /// <summary>
    /// Result of loading change tracker from prior build
    /// </summary>
    public sealed class LoadingTrackerResult
    {
        /// <summary>
        /// Whether the prior changing tracking set was loaded
        /// </summary>
        public bool Succeeded => Status == LoadingTrackerStatus.Success;

        /// <summary>
        /// The loaded change tracking set
        /// </summary>
        public readonly FileChangeTrackingSet ChangeTrackingSet;

        /// <summary>
        /// Duration
        /// </summary>
        public long DurationMs { get; private set; }

        /// <summary>
        /// The status of loading the change tracker
        /// </summary>
        public readonly LoadingTrackerStatus Status;

        /// <summary>
        /// Number of tracked volumes
        /// </summary>
        public int TrackedVolumesCount { get; private set; }

        /// <summary>
        /// Total journal size of the tracked volumes
        /// </summary>
        public ulong TrackedJournalsSizeBytes { get; private set; }

        /// <summary>
        /// File envelope id.
        /// </summary>
        public FileEnvelopeId FileId { get; }

        private ulong m_firstFailedVolume;

        private string m_failureMessage;

        private Usn m_nextUsn;

        private Usn m_checkPointUsn;

        /// <summary>
        /// Return a description for each status
        /// </summary>
        [SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider")]
        internal string StatusAsString
        {
            get
            {
                switch (Status)
                {
                    case LoadingTrackerStatus.Success:
                        return I($"Successfully loaded the tracker, with {TrackedVolumesCount} tracked volumes, and with journal size {TrackedJournalsSizeBytes} bytes");
                    case LoadingTrackerStatus.PriorTrackerDisabled:
                        return "The tracker was disabled in the prior build";
                    case LoadingTrackerStatus.MissingVolumeJournal:
                        return I($"Change tracking set is unusable since the required volume '{m_firstFailedVolume:X16}' is not present or does not have its journal available");
                    case LoadingTrackerStatus.JournalIdMismatch:
                        return I($"Change tracking set is unusable since the journal of the volume '{m_firstFailedVolume:X16}' has been changed");
                    case LoadingTrackerStatus.BadFormatMarker:
                        return I($"Bad format marker or incompatible version: {m_failureMessage ?? string.Empty}");
                    case LoadingTrackerStatus.TrackingSetCannotBeOpened:
                        return I($"Change tracking set not found: {m_failureMessage ?? string.Empty}");
                    case LoadingTrackerStatus.LoadException:
                        return m_failureMessage ?? string.Empty;
                    case LoadingTrackerStatus.BuildEngineFingerprintMismatch:
                        return I($"Change tracking set could not be loaded due to build engine fingerprint mismatch.");
                    case LoadingTrackerStatus.JournalGoesBackInTime:
                        return I($"Journal goes back in time because the next Usn '{m_nextUsn}' is smaller than the checkpoint Usn '{m_checkPointUsn}'");
                    default:
                        throw Contract.AssertFailure(I($"Unrecognized {nameof(LoadingTrackerStatus)}"));
                }
            }
        }

        private LoadingTrackerResult(FileEnvelopeId fileId, LoadingTrackerStatus status, FileChangeTrackingSet changeTrackingSet)
        {
            Contract.Requires(
                (changeTrackingSet == null && status != LoadingTrackerStatus.Success)
                || (changeTrackingSet != null && status == LoadingTrackerStatus.Success),
                I($"{nameof(changeTrackingSet)} is null: {changeTrackingSet == null}, status: {status}"));
            Contract.Requires(status != LoadingTrackerStatus.Success || fileId.IsValid);

            FileId = fileId;
            Status = status;
            ChangeTrackingSet = changeTrackingSet;
        }

        /// <summary>
        /// Creates an instance of <see cref="LoadingTrackerResult"/> for successful loading.
        /// </summary>
        public static LoadingTrackerResult Success(
            FileEnvelopeId fileId,
            FileChangeTrackingSet changeTrackingSet,
            int trackedVolumesCount,
            ulong trackedJournalsSizeBytes,
            long durationMs)
        {
            Contract.Requires(fileId.IsValid);
            Contract.Requires(changeTrackingSet != null);

            return new LoadingTrackerResult(fileId, LoadingTrackerStatus.Success, changeTrackingSet)
            {
                TrackedVolumesCount = trackedVolumesCount,
                TrackedJournalsSizeBytes = trackedJournalsSizeBytes,
                DurationMs = durationMs,
            };
        }

        /// <summary>
        /// Creates an instance of <see cref="LoadingTrackerResult"/> for failure due to disabled prior tracker.
        /// </summary>
        public static LoadingTrackerResult FailPriorTrackerDisabled()
        {
            return new LoadingTrackerResult(FileEnvelopeId.Invalid, LoadingTrackerStatus.PriorTrackerDisabled, null);
        }

        /// <summary>
        /// Creates an instance of <see cref="LoadingTrackerResult"/> for failure due to missing volume journal.
        /// </summary>
        public static LoadingTrackerResult FailMissingVolumeJournal(ulong firstFailedVolume)
        {
            return new LoadingTrackerResult(FileEnvelopeId.Invalid, LoadingTrackerStatus.MissingVolumeJournal, null)
            {
                m_firstFailedVolume = firstFailedVolume,
            };
        }

        /// <summary>
        /// Creates an instance of <see cref="LoadingTrackerResult"/> for failure due to journal ID mismatch
        /// </summary>
        public static LoadingTrackerResult FailJournalIdMismatch(ulong firstFailedVolume)
        {
            return new LoadingTrackerResult(FileEnvelopeId.Invalid, LoadingTrackerStatus.JournalIdMismatch, null)
            {
                m_firstFailedVolume = firstFailedVolume,
            };
        }

        /// <summary>
        /// Creates an instance of <see cref="LoadingTrackerResult"/> for failure due to journal goes back in time.
        /// </summary>
        public static LoadingTrackerResult FailJournalGoesBackInTime(ulong firstFailedVolume, Usn nextUsn, Usn checkPointUsn)
        {
            return new LoadingTrackerResult(FileEnvelopeId.Invalid, LoadingTrackerStatus.JournalIdMismatch, null)
            {
                m_firstFailedVolume = firstFailedVolume,
                m_nextUsn = nextUsn,
                m_checkPointUsn = checkPointUsn
            };
        }

        /// <summary>
        /// Creates an instance of <see cref="LoadingTrackerResult"/> for failure due to bad format marker.
        /// </summary>
        public static LoadingTrackerResult FailBadFormatMarker(string failureMessage)
        {
            return new LoadingTrackerResult(FileEnvelopeId.Invalid, LoadingTrackerStatus.BadFormatMarker, null)
            {
                m_failureMessage = failureMessage,
            };
        }

        /// <summary>
        /// Creates an instance of <see cref="LoadingTrackerResult"/> for failure because tracking set cannot be opened.
        /// </summary>
        public static LoadingTrackerResult FailTrackingSetCannotBeOpened(string failureMessage)
        {
            return new LoadingTrackerResult(FileEnvelopeId.Invalid, LoadingTrackerStatus.TrackingSetCannotBeOpened, null) { m_failureMessage = failureMessage };
        }

        /// <summary>
        /// Creates an instance of <see cref="LoadingTrackerResult"/> for failure due to exception.
        /// </summary>
        public static LoadingTrackerResult FailLoadException(string failureMessage)
        {
            return new LoadingTrackerResult(FileEnvelopeId.Invalid, LoadingTrackerStatus.LoadException, null) { m_failureMessage = failureMessage };
        }

        /// <summary>
        /// Creates an instance of <see cref="LoadingTrackerResult"/> for failure due to build engine fingerprint mismatch.
        /// </summary>
        public static LoadingTrackerResult FailBuildEngineFingerprintMismatch()
        {
            return new LoadingTrackerResult(FileEnvelopeId.Invalid,  LoadingTrackerStatus.BuildEngineFingerprintMismatch, null);
        }
    }

    /// <summary>
    /// Status of reloading change tracker
    /// </summary>
    /// <remarks>
    /// Add a new description to <see cref="LoadingTrackerResult.StatusAsString"/> when a new value is added
    /// </remarks>
    public enum LoadingTrackerStatus
    {
        /// <summary>
        /// Success
        /// </summary>
        Success,

        /// <summary>
        /// Tracker is disabled in the prior build
        /// </summary>
        PriorTrackerDisabled,

        /// <summary>
        /// Journal is missing for one of the required volumes
        /// </summary>
        MissingVolumeJournal,

        /// <summary>
        /// Journal id mismatch for one of the required volumes
        /// </summary>
        JournalIdMismatch,

        /// <summary>
        /// Bad format marker
        /// </summary>
        BadFormatMarker,

        /// <summary>
        /// Tracking set cannot be opened
        /// </summary>
        TrackingSetCannotBeOpened,

        /// <summary>
        /// Exception occured during loading
        /// </summary>
        LoadException,

        /// <summary>
        /// Failed to load due to build engine fingerprint mismatch
        /// </summary>
        BuildEngineFingerprintMismatch,

        /// <summary>
        /// Journal goes back in time because the next USN is smaller than the checkpoint.
        /// </summary>
        JournalGoesBackInTime
    }

    /// <summary>
    /// Represent the result of <see cref="FileChangeTracker.TryProcessChanges"/> method
    /// </summary>
    public sealed class ScanningJournalResult
    {
        /// <summary>
        /// NotChecked instance for <see cref="ScanningJournalResult"/>
        /// </summary>
        public static readonly ScanningJournalResult NotChecked = new ScanningJournalResult(
            ScanningJournalStatus.NotChecked,
            new CounterCollection<ReadJournalCounter>());

        /// <summary>
        /// Whether the journal was read with no errors
        /// </summary>
        public bool Succeeded => Status == ScanningJournalStatus.Success;

        /// <summary>
        /// Reason for failure to read the journal
        /// </summary>
        public readonly ScanningJournalStatus Status;

        /// <summary>
        /// Statistics.
        /// </summary>
        public readonly CounterCollection<ReadJournalCounter> Stats;

        /// <summary>
        /// Total duration.
        /// </summary>
        public TimeSpan TotalDuration
            =>
                Stats.GetElapsedTime(ReadJournalCounter.ReadRelevantJournalDuration) +
                Stats.GetElapsedTime(ReadJournalCounter.FalsePositiveValidationDuration);

        private ScanningJournalResult(ScanningJournalStatus status, CounterCollection<ReadJournalCounter> stats)
        {
            Contract.Requires(stats != null);

            Status = status;
            Stats = stats;
        }

        /// <summary>
        /// Create a failed <see cref="ScanningJournalResult"/> instance
        /// </summary>
        public static ScanningJournalResult Fail(ScanningJournalStatus reason, CounterCollection<ReadJournalCounter> stats = null)
        {
            Contract.Requires(reason.Failed());

            return new ScanningJournalResult(reason, stats ?? new CounterCollection<ReadJournalCounter>());
        }

        /// <summary>
        /// Create a succeeded <see cref="ScanningJournalResult"/> instance
        /// </summary>
        public static ScanningJournalResult Success(CounterCollection<ReadJournalCounter> stats)
        {
            Contract.Requires(stats != null);

            return new ScanningJournalResult(ScanningJournalStatus.Success, stats);
        }

        /// <summary>
        /// Adding the statistics of reading journals (esp. journals from different volumes)
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2225")]
        [SuppressMessage("Microsoft.Design", "CA1013")]
        public static ScanningJournalResult operator +(ScanningJournalResult x, ScanningJournalResult y)
        {
            var status = x.Status > y.Status ? x.Status : y.Status;
            var stats = x.Stats + y.Stats;

            return new ScanningJournalResult(status, stats);
        }
    }

    /// <summary>
    /// Status of scanning change journal
    /// </summary>
    public enum ScanningJournalStatus
    {
        /// <summary>
        /// Not checked
        /// </summary>
        NotChecked = 0,

        /// <summary>
        /// Success
        /// </summary>
        Success,

        /// <summary>
        /// Failed to open volume handle
        /// </summary>
        FailedToOpenVolumeHandle,

        /// <summary>
        /// Protocol error
        /// </summary>
        ProtocolError,

        /// <summary>
        /// Journal is not active
        /// </summary>
        JournalNotActive,

        /// <summary>
        /// Journal is overwritten and some entries are deleted
        /// </summary>
        JournalEntryDeleted,

        /// <summary>
        /// Journal is being deleted
        /// </summary>
        JournalDeleteInProgress,

        /// <summary>
        /// Incorrect parameter error happens when the volume format is broken.
        /// </summary>
        InvalidParameter,

        /// <summary>
        /// The queried volume does not support a change journal
        /// </summary>
        VolumeDoesNotSupportChangeJournals,

        /// <summary>
        /// Journal scanning reached timeout.
        /// </summary>
        Timeout,
    }

    /// <summary>
    /// Extensions for <see cref="ScanningJournalStatus"/>
    /// </summary>
    public static class ScanningJournalStatusExtensions
    {
        /// <summary>
        /// Checks if journal scanning failed.
        /// </summary>
        public static bool Failed(this ScanningJournalStatus status)
            => status != ScanningJournalStatus.NotChecked && status != ScanningJournalStatus.Success;

        /// <summary>
        /// Gets a description of <see cref="ScanningJournalStatus"/>.
        /// </summary>
        public static string AsString(this ScanningJournalStatus status)
        {
            switch (status)
            {
                case ScanningJournalStatus.NotChecked:
                    return "Not checked";
                case ScanningJournalStatus.Success:
                    return "Success";
                case ScanningJournalStatus.FailedToOpenVolumeHandle:
                    return "Failed to open volume handle";
                case ScanningJournalStatus.ProtocolError:
                    return "Protocol error";
                case ScanningJournalStatus.JournalNotActive:
                    return "Journal is not active";
                case ScanningJournalStatus.JournalEntryDeleted:
                    return "Journal is overwritten and some entries are deleted";
                case ScanningJournalStatus.JournalDeleteInProgress:
                    return "Journal is being deleted";
                case ScanningJournalStatus.InvalidParameter:
                    return "Incorrect parameter possibly due to broken volume format";
                case ScanningJournalStatus.VolumeDoesNotSupportChangeJournals:
                    return "The queried volume does not support a change journal";
                case ScanningJournalStatus.Timeout:
                    return "Journal scanning times out";
                default:
                    throw Contract.AssertFailure(I($"Unrecognized {nameof(ScanningJournalStatus)}"));
            }
        }
    }

    /// <summary>
    /// Exception given to the observer of <see cref="FileChangeTrackingSet" /> on journal scanning error.
    /// </summary>
    public sealed class ScanningJournalException : Exception
    {
        /// <summary>
        /// Result indicating failure.
        /// </summary>
        public readonly ScanningJournalResult Result;

        /// <summary>
        /// Creates an instance of <see cref="ScanningJournalException" />.
        /// </summary>
        public ScanningJournalException(ScanningJournalResult result)
            : base(string.Empty)
        {
            Contract.Requires(result.Status.Failed());
            Result = result;
        }
    }

    /// <summary>
    /// Represent statistics about the usage of USN change journal
    /// </summary>
    /// <remarks>
    /// This class is used for telemetry
    /// </remarks>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public readonly struct JournalProcessingStatistics
    {
        /// <summary>
        /// Results of scanning journal.
        /// </summary>
        public readonly ScanningJournalResult ScanningJournalResult;

        /// <summary>
        /// Delegate for logging these statistics as a message.
        /// </summary>
        public delegate void LogMessage(LoggingContext loggingContext, string message);

        /// <summary>
        /// Delegate for logging statistics.
        /// </summary>
        public delegate void LogStats(LoggingContext loggingContext, string scanningStatus, IDictionary<string, long> stats);

        /// <summary>
        /// Creating a <see cref="JournalProcessingStatistics"/> struct with given results from three phases: loading tracker, scanning journal, saving tracker
        /// </summary>
        public JournalProcessingStatistics(ScanningJournalResult scanningJournalResult)
        {
            ScanningJournalResult = scanningJournalResult;
        }

        /// <summary>
        /// Logs <see cref="JournalProcessingStatistics"/> both for local and telemetry.
        /// </summary>
        public void Log(LoggingContext loggingContext, LogMessage logMessage = null, LogStats logStats = null)
        {
            var stats = ScanningJournalResult.Stats.AsStatistics();
            stats["ScanningJournalResultTotalDuration"] = (long)ScanningJournalResult.TotalDuration.TotalMilliseconds;
            var message = I($"ScanningJournalStatus: {ScanningJournalResult.Status}");
            message += Environment.NewLine + string.Join(Environment.NewLine, stats.Select(kvp => "    " + kvp.Key + ": " + kvp.Value));

            logMessage?.Invoke(loggingContext, message);
            logStats?.Invoke(
                loggingContext,
                ScanningJournalResult.Status.ToString(),
                stats);
        }
    }

    #endregion
}
