// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Ninja.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
    #pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId
    {
        None = 0,

        // reserved 11500 .. 11600 for ninja
        InvalidResolverSettings = 11500,
        ProjectRootDirectoryDoesNotExist = 11501,
        NinjaSpecFileDoesNotExist = 11502,
        GraphConstructionInternalError = 11503,
        GraphConstructionFinishedSuccessfullyButWithWarnings = 11504,
        InvalidExecutablePath = 11505,
        PipSchedulingFailed = 11506,
        UnexpectedPipConstructorException = 11507,
        CouldNotDeleteToolArgumentsFile = 11508,
        CouldNotComputeRelativePathToSpec = 11609,
        LeftGraphToolOutputAt = 11610,

    }
}
