// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.FrontEnd.JavaScript.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
    #pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId
    {
        None = 0,

        // reserved 11900 .. 12000 for JavaScript
        InvalidResolverSettings = 11900,
        ProjectGraphConstructionError = 11901,
        ProjectIsIgnoredScriptIsMissing = 11902,
        CannotDeleteSerializedGraphFile = 11903,
        DependencyIsIgnoredScriptIsMissing = 11904,
        SchedulingPipFailure = 11905,
        UnexpectedPipBuilderException = 11906,
        GraphConstructionFinishedSuccessfullyButWithWarnings = 11907,
        GraphBuilderFilesAreNotRemoved = 11908,
        JavaScriptCommandIsEmpty = 11909,
        JavaScriptCommandIsDuplicated = 11910,
        CycleInJavaScriptCommands = 11911,
        CannotFindGraphBuilderTool = 11912,
        // Was: SpecifiedCommandForExportDoesNotExist = 11913,
        SpecifiedPackageForExportDoesNotExist = 11914,
        RequestedExportIsNotPresent = 11915,
        SpecifiedExportIsAReservedName = 11916,
        ConstructingGraphScript = 11917,
        JavaScriptCommandGroupCanOnlyContainRegularCommands = 11918,
        CustomScriptsFailure = 11919,
        CannotLoadScriptsFromJsonFile = 11920,
        InvalidRegexInProjectSelector = 11921,
    }
}
