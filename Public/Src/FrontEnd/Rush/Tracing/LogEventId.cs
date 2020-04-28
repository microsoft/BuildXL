// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.FrontEnd.Rush.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
    #pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId
    {
        None = 0,

        // reserved 11700 .. 11800 for Rush front-end
        InvalidResolverSettings = 11700,
        ProjectGraphConstructionError = 11701,
        ProjectIsIgnoredScriptIsMissing = 11702,
        CannotDeleteSerializedGraphFile = 11703,
        DependencyIsIgnoredScriptIsMissing = 11704,
        SchedulingPipFailure = 11705,
        UnexpectedPipBuilderException = 11706,
        GraphConstructionFinishedSuccessfullyButWithWarnings = 11707,
        GraphBuilderFilesAreNotRemoved = 11708,
        RushCommandIsEmpty = 11709,
        RushCommandIsDuplicated = 11710,
        CycleInRushCommands = 11711,
        CannotFindRushLib = 11712,
        UsingRushLibBaseAt = 11713,
        SpecifiedCommandForExportDoesNotExist = 11714,
        SpecifiedPackageForExportDoesNotExist = 11715,
        RequestedExportIsNotPresent = 11716,
        SpecifiedExportIsAReservedName = 11717,
    }
}
