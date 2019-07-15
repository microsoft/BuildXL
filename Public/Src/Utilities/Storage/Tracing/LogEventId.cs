// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Storage.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId
    {
        None = 0,

        // USN/Change Journal usage (FileChangeTracker)
        StartLoadingChangeTracker = 680,
        StartScanningJournal = 681,
        ScanningJournal = 682, // was EndSavingChangeTracker = 682,
        // was ChangeTrackerNotLoaded = 683,
        // was ScanningJournalError = 684,
        EndLoadingChangeTracker = 685,
        EndScanningJournal = 686,
        LoadingChangeTracker = 687,
        DisableChangeTracker = 688,

        //// Storage
        StorageFileContentTableIgnoringFileSinceVersionedFileIdentityIsNotSupported = 700, // was StorageFileContentTableIgnoringFileSinceUsnJournalDisabled
        StorageLoadFileContentTable = 701,
        StorageCacheCopyLocalError = 706,
        StorageCacheStartupError = 712,

        //// USNs
        StorageKnownUsnHit = 714,
        StorageUnknownUsnMiss = 715,
        StorageRecordNewKnownUsn = 717,
        StorageUnknownFileMiss = 718,
        StorageVersionedFileIdentityNotSupportedMiss = 719, // StorageJournalDisabledMiss

        StorageVolumeCollision = 723,
        StorageTryOpenOrCreateFileFailure = 724,

        StorageCacheContentPinned = 725,

        FileMaterializationMismatchFileExistenceResult = 741,

        StorageUsnMismatchButContentMatch = 932,

        FileCombinerVersionIncremented = 2103,
        FileCombinerFailedToInitialize = 2104,
        FileCombinerFailedToCreate = 2106,
        SpecCache = 2107,
        IncrementalFrontendCache = 2108,

        // Change journal service
        ChangeJournalServiceRequestStart = 4000,
        ChangeJournalServiceRequestStop = 4001,
        ChangeJournalPipeServerInstanceThreadCrash = 4002,
        ChangeJournalServiceRequestIOError = 4003,
        ChangeJournalServiceProtocolError = 4004,
        ChangeJournalServiceReadJournalRequest = 4005,
        ChangeJournalServiceQueryJournalRequest = 4006,
        ChangeJournalServiceQueryServiceVersionRequest = 4007,
        ChangeJournalServiceUnsupportedProtocolVersion = 4008,

        ValidateJunctionRoot = 4202,
        ConflictDirectoryMembershipFingerprint = 4205,

        // Change detection (FileChangeTrackingSet)
        ChangeDetectionProbeSnapshotInconsistent = 8002,
        ChangeDetectionComputedDirectoryMembershipTrackingFingerprint = 8003,
        ChangeDetectionDueToPerpetualDirtyNode = 8004,
        // was ChangeDetectionSaveTrackingSet = 8005,

        ChangeDetectionFailCreateTrackingSetDueToJournalQueryError = 8006,
        ChangeDetectionCreateResult = 8007,

        ChangeDetectionSingleVolumeScanJournalResult = 8008,
        ChangeDetectionScanJournalFailedSinceJournalGotOverwritten = 8009,
        ChangeDetectionScanJournalFailedSinceTimeout = 8010,

        AntiDependencyValidationPotentiallyAddedButVerifiedAbsent = 8011,
        AntiDependencyValidationFailedRetrackPathAsNonExistent = 8012,
        AntiDependencyValidationFailedProbePathToVerifyNonExistent = 8013,
        AntiDependencyValidationStats = 8014,

        EnumerationDependencyValidationPotentiallyAddedOrRemovedDirectChildrenButVerifiedUnchanged = 8015,
        EnumerationDependencyValidationFailedRetrackUnchangedDirectoryForMembershipChanges = 8016,
        EnumerationDependencyValidationFailedToOpenOrEnumerateDirectoryForMembershipChanges = 8017,
        EnumerationDependencyValidationStats = 8018,

        HardLinkValidationPotentiallyChangedButVerifiedUnchanged = 8019,
        HardLinkValidationHardLinkChangedBecauseFileIdChanged = 8020,
        HardLinkValidationFailedRetrackUnchangedHardLink = 8021,
        HardLinkValidationFailedToOpenHardLinkDueToNonExistent = 8022,
        HardLinkValidationFailedToOpenOrTrackHardLink = 8023,
        HardLinkValidationStats = 8024,

        ChangeDetectionSingleVolumeScanJournalResultTelemetry = 8025,
        ChangedPathsDetectedByJournalScanning = 8026,
        ChangeDetectionParentPathIsUntrackedOnTrackingAbsentRelativePath = 8027,
        IgnoredRecordsDueToUnchangedJunctionRootCount = 8028,
        TrackChangesToFileDiagnostic = 8029,

        StartSavingChangeTracker = 8030,
        EndSavingChangeTracker = 8031,
        SavingChangeTracker = 8032,
    }
}
