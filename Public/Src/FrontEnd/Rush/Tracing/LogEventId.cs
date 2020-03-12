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
        GraphConstructionInternalError = 11702,
        CannotDeleteSerializedGraphFile = 11703,
        CycleInBuildTargets = 11704,
        SchedulingPipFailure = 11705,
        UnexpectedPipBuilderException = 11706,
        GraphConstructionFinishedSuccessfullyButWithWarnings = 11707,
        GraphBuilderFilesAreNotRemoved = 11708,
    }
}
