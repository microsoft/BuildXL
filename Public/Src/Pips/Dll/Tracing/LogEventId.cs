// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Pips.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId : ushort
    {
        None = 0,

        InvalidOutputSinceOutputIsSource = 206,
        InvalidOutputSincePreviousVersionUsedAsInput = 207,
        InvalidOutputSinceOutputHasUnexpectedlyHighWriteCount = 208,
        InvalidOutputSinceRewritingOldVersion = 209,
        InvalidOutputSinceRewrittenOutputMismatchedWithInput = 210,
        InvalidOutputDueToSimpleDoubleWrite = 211,
        InvalidOutputDueToMultipleConflictingRewriteCounts = 212,
        InvalidInputSincePathIsWrittenAndThusNotSource = 213,
        InvalidInputSinceInputIsRewritten = 215,
        InvalidInputDueToMultipleConflictingRewriteCounts = 216,
        InvalidInputSinceInputIsOutputWithNoProducer = 217,
        InvalidProcessPipDueToNoOutputArtifacts = 218,

        InvalidCopyFilePipDueToSameSourceAndDestinationPath = 220,
        InvalidWriteFilePipSinceOutputIsRewritten = 221,
        InvalidOutputSinceDirectoryHasBeenSealed = 246,
        InvalidSealDirectoryContentSinceNotUnderRoot = 247,
        InvalidSealDirectoryDirectoryOutputContentSinceNotUnderRoot = 14412,
        ScheduleFailAddPipInvalidSealDirectoryContentIsNotOutput = 14413,
        InvalidOutputSinceFileHasBeenPartiallySealed = 248,

        StartFilterApplyTraversal = 282,
        EndFilterApplyTraversal = 283,
        InvalidSealDirectorySourceNotUnderMount = 284,
        InvalidSealDirectorySourceNotUnderReadableMount = 285,

        NoPipsMatchedFilter = 295,
        InvalidPipDueToInvalidServicePipDependency = 296,
        ScheduleFailAddPipDueToInvalidPreserveOutputAllowlist = 302,
        ScheduleFailAddPipDueToInvalidAllowPreserveOutputsFlag = 303,
        InvalidInputSinceCorrespondingOutputIsTemporary = 310,

        InvalidOutputUnderNonWritableRoot = 2000,
        InvalidInputUnderNonReadableRoot = 2001,
        InvalidTempDirectoryUnderNonWritableRoot = 2003,
        InvalidTempDirectoryInvalidPath = 2004,
        RewritingPreservedOutput = 2005,

        FailedToAddFragmentPipToGraph = 5048,
        ExceptionOnAddingFragmentPipToGraph = 5049,
        ExceptionOnDeserializingPipGraphFragment = 5050,
        DeserializationStatsPipGraphFragment = 5051,

        InvalidOutputSinceDirectoryHasBeenProducedByAnotherPip = 13100,
        InvalidOutputSinceOutputIsBothSpecifiedAsFileAndDirectory = 13101,
        SourceDirectoryUsedAsDependency = 13102,

        // Graph validation.

        InvalidGraphSinceOutputDirectoryContainsSourceFile = 14100,
        InvalidGraphSinceOutputDirectoryContainsOutputFile = 14101,
        InvalidGraphSinceOutputDirectoryContainsSealedDirectory = 14102,

        InvalidGraphSinceFullySealedDirectoryIncomplete = 14103,
        InvalidGraphSinceFullySealedDirectoryIncompleteDueToMissingOutputDirectories = 14114,
        InvalidGraphSinceSourceSealedDirectoryContainsOutputFile = 14104,
        InvalidGraphSinceSourceSealedDirectoryContainsOutputDirectory = 14105,
        InvalidGraphSinceSharedOpaqueDirectoryContainsExclusiveOpaqueDirectory = 14106,
        InvalidGraphSinceOutputDirectoryCoincidesSourceFile = 14107,
        InvalidGraphSinceOutputDirectoryCoincidesOutputFile = 14108,
        InvalidGraphSinceOutputDirectoryCoincidesSealedDirectory = 14109,
        InvalidGraphSinceSourceSealedDirectoryCoincidesSourceFile = 14110,
        InvalidGraphSinceSourceSealedDirectoryCoincidesOutputFile = 14111,
        PreserveOutputsDoNotApplyToSharedOpaques = 14112,
        InvalidGraphSinceArtifactPathOverlapsTempPath = 14113,

        InvalidSharedOpaqueDirectoryDueToOverlap = 14401,
        ScheduleFailAddPipInvalidComposedSealDirectoryNotUnderRoot = 14402,
        ScheduleFailAddPipInvalidComposedSealDirectoryIsNotSharedOpaque = 14403,
        ScheduleFailAddPipInvalidComposedSealDirectoryDoesNotContainRoot = 14416,

        PerformanceDataCacheTrace = 14409,
        PipStaticFingerprint = 14410,

        MultiplePipsUsingSameTemporaryDirectory = 14411,
        ScheduleFailAddPipAssertionNotSupportedInCompositeOpaques = 14414,
        WriteDeclaredOutsideOfKnownMount = 14415,
    }
}
