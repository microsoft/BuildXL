// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Native.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId
    {
        None = 0,
        
        // Storage
        FileUtilitiesDirectoryDeleteFailed = 698,
        FileUtilitiesDiagnostic = 699,
        StorageFileContentTableIgnoringFileSinceVersionedFileIdentityIsNotSupported = 700, // was StorageFileContentTableIgnoringFileSinceUsnJournalDisabled
        StorageLoadFileContentTable = 701,
        StorageHashedSourceFile = 702,
        StorageUsingKnownHashForSourceFile = 703,
        SettingOwnershipAndAcl = 704,
        SettingOwnershipAndAclFailed = 705,
        StorageCacheCopyLocalError = 706,
        // Reserved = 707,
        StorageCacheGetContentError = 708,
        // Reserved = 709,
        // Reserved = 710,
        StorageCachePutContentFailed = 711,
        StorageCacheStartupError = 712,

        // USNs
        StorageReadUsn = 713,
        StorageKnownUsnHit = 714,
        StorageUnknownUsnMiss = 715,
        StorageCheckpointUsn = 716,
        StorageRecordNewKnownUsn = 717,
        StorageUnknownFileMiss = 718,
        StorageVersionedFileIdentityNotSupportedMiss = 719, // StorageJournalDisabledMiss

        StorageTryOpenDirectoryFailure = 720,
        StorageFoundVolume = 721,
        StorageTryOpenFileByIdFailure = 722,
        StorageVolumeCollision = 723,
        StorageTryOpenOrCreateFileFailure = 724,
        DetouredProcessAccessViolationException = 734,


        RetryOnFailureException = 744,

        MoreBytesWrittenThanBufferSize = 2930,

        //// Container related errors
        FailedToCleanUpContainer = 12203,
        WarningSettingUpContainer = 12204,
    }
}
