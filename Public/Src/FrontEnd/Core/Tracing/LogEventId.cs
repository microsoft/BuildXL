// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Core.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId : ushort
    {
        None = 0,

        // Phases
        FrontEndLoadConfigPhaseStart = 2826,
        FrontEndLoadConfigPhaseComplete = 2827,
        FrontEndInitializeResolversPhaseStart = 2828,
        FrontEndInitializeResolversPhaseComplete = 2829,

        // 2830 and beyond are reserved in engine

        // reserved 11200..11299
        UnregisteredResolverKind = 11200,
        UnableToFindFrontEndToParse = 11201,
        UnableToFindFrontEndToEvaluate = 11202,
        UnableToEvaluateUnparsedFile = 11203,
        UnableToFindFrontEndToAnalyze = 11204,
        UnableToAnalyzeUnparsedFile = 11205,
        DEPRICATEDWaitForTypeCheckingToFinish = 11206, // have to mark deprecated since all numbers are not explicit and commenting out would renumber all messages.
        StartDownloadingTool = 11207,
        EndDownloadingTool = 11208,
        DownloadToolFailedToHashExisting = 11209,
        DownloadToolErrorInvalidUri = 11210,
        DownloadToolErrorCopyFile = 11211,
        DownloadToolFailedDueToCacheError = 11212,
        DownloadToolIsUpToDate = 11213,
        DownloadToolIsRetrievedFromCache = 11214,
        DownloadToolErrorDownloading = 11215,
        DownloadToolErrorFileNotDownloaded = 11216,
        DownloadToolErrorDownloadedToolWrongHash = 11217,
        DownloadToolWarnCouldntHashDownloadedFile = 11218,
        DownloadToolCannotCache = 11219,
        StartRetrievingPackage = 11220,
        EndRetrievingPackage = 11221,
        DownloadPackageFailedDueToCacheError = 11222,
        DownloadPackageFailedDueToInvalidCacheContents = 11223,
        DownloadPackageCannotCacheError = 11224,
        DownloadPackageCouldntHashPackageFile = 11225,
        PackageRestoredFromCache = 11226,
        PackageNotFoundInCacheAndDownloaded = 11227,
        PackageNotFoundInCacheAndStartedDownloading = 11228,
        PrimaryConfigFileNotFound = 11229,
        CannotBuildWorkspace = 11230,
        CheckerError = 11231,
        CheckerGlobalError = 11232,
        TypeScriptSyntaxError = 11233,
        TypeScriptLocalBindingError = 11234,

        PackagePresumedUpToDate = 11235,
        FrontEndParsePhaseStart = 11236,
        FrontEndParsePhaseComplete = 11237,
        FrontEndConvertPhaseStart = 11238,
        FrontEndConvertPhaseProgress = 11239,
        FrontEndConvertPhaseComplete = 11240,
        FrontEndConvertPhaseCanceled = 11241,
        FrontEndStartEvaluateValues = 11242,
        FrontEndEndEvaluateValues = 11243,
        FrontEndEvaluateValuesProgress = 11244,
        FrontEndEvaluatePhaseCanceled = 11245,
        FrontEndBuildWorkspacePhaseStart = 11246,
        FrontEndBuildWorkspacePhaseProgress = 11247,
        FrontEndBuildWorkspacePhaseComplete = 11248,
        FrontEndBuildWorkspacePhaseCanceled = 11249,
        ErrorNotFoundNamedQualifier = 11250,
        ErrorNonExistenceNamedQualifier = 11251,
        FrontEndWorkspaceAnalysisPhaseStart = 11252,
        FrontEndWorkspaceAnalysisPhaseProgress = 11253,
        FrontEndWorkspaceAnalysisPhaseComplete = 11254,
        FrontEndWorkspaceAnalysisPhaseCanceled = 11255,
        FailedToConvertModuleToEvaluationModel = 11256,
        GraphPartiallyReloaded = 11257,
        GraphPatchingDetails = 11258,

        // Events related to incrementality
        SaveFrontEndSnapshot = 11259,
        SaveFrontEndSnapshotError = 11260,

        LoadFrontEndSnapshot = 11261,
        LoadFrontEndSnapshotError = 11262,

        FailToReuseFrontEndSnapshot = 11263,

        TryingToReuseFrontEndSnapshot = 11264,
        BuildingFullWorkspace = 11265,

        WorkspaceDefinitionCreated = 11266,
        WorkspaceFiltered = 11267,
        WorkspaceDefinitionFiltered = 11268,
        SlowestScriptElements = 11269,
        CycleDetectionStatistics = 11270,

        FailedToFilterWorkspaceDefinition = 11271,
        LargestScriptFiles = 11272,
        ScriptFilesReloadedWithNoWarningsOrErrors = 11273,
        ReportDestructionCone = 11274,

        // Package stuff continued
        PackageCacheMissInformation = 11275,

        CheckerWarning = 11276,
        CheckerGlobalWarning = 11277,

        PackagePresumedUpToDateWithoutHashComparison = 11278,
        CanNotReusePackageHashFile = 11279,
        CanNotUpdatePackageHashFile = 11280,
        CanNotRestorePackagesDueToCacheError = 11281,
        ForcePopulateTheCacheOptionWasSpecified = 11282,
        WorkspaceDefinitionFilteredBasedOnModuleFilter = 11283,
        ErrorIllFormedQualfierExpresion = 11284,
        ErrorEmptyQualfierExpresion = 11285,
        ErrorNoQualifierValues = 11286,
        ErrorNamedQualifierNoValues = 11287,
        ErrorNamedQualifierInvalidKey = 11288,
        ErrorNamedQualifierInvalidValue = 11289,
        // Reserved: 11290,
        ErrorDefaultQualiferInvalidKey = 11291,
        ErrorDefaultQualifierInvalidValue = 11292,

        // Note: DownloadPackageCannotCacheError = 11224,
        DownloadPackageCannotCacheWarning = 11293,
        FrontEndEvaluateFragmentsProgress = 11294,

        MaterializingFileToFileDepdencyMap = 2917,
        ErrorMaterializingFileToFileDepdencyMap = 2918,
    }
}
