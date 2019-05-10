// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.MsBuild.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
    #pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId
    {
        None = 0,

        // reserved 11400 .. 11500 for MSBuild front-end
        InvalidResolverSettings = 11400,
        ProjectGraphConstructionError = 11401,
        CannotFindMsBuildAssembly = 11402,
        UncoordinatedMsBuildAssemblyLocations = 11403,
        NoSearchLocationsSpecified = 11404,
        CannotParseBuildParameterPath = 11405,
        LaunchingGraphConstructionTool = 11407,
        CannotFindParsingEntryPoint = 11408,
        TooManyParsingEntryPointCandidates = 11409,
        GraphConstructionInternalError = 11410,
        CannotDeleteSerializedGraphFile = 11411,
        GraphConstructionToolCompleted = 11412,
        CycleInBuildTargets = 11413,
        SchedulingPipFailure = 11414,
        UnexpectedPipBuilderException = 11415,
        ReportGraphConstructionProgress = 11416,
        CannotGetProgressFromGraphConstructionDueToTimeout = 11417,
        CannotGetProgressFromGraphConstructionDueToUnexpectedException = 11418,
        GraphConstructionFinishedSuccessfullyButWithWarnings = 11419,
        CannotDeleteResponseFile = 11420,
        ProjectWithEmptyTargetsIsNotScheduled = 11421,
        ProjectIsNotSpecifyingTheProjectReferenceProtocol = 11422,
        GraphBuilderFilesAreNotRemoved = 11423,
        LeafProjectIsNotSpecifyingTheProjectReferenceProtocol = 11424,
        ProjectPredictedTargetsAlsoContainDefaultTargets = 11425,
    }
}
