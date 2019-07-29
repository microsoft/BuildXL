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

        // reserved 11550 .. 11600 for ninja
        InvalidResolverSettings = 11550,
        ProjectRootDirectoryDoesNotExist = 11551,
        NinjaSpecFileDoesNotExist = 11552,
        GraphConstructionInternalError = 11553,
        GraphConstructionFinishedSuccessfullyButWithWarnings = 11554,
        InvalidExecutablePath = 11555,
        PipSchedulingFailed = 11556,
        UnexpectedPipConstructorException = 11557,
        CouldNotDeleteToolArgumentsFile = 11558,
        CouldNotComputeRelativePathToSpec = 11659,
        LeftGraphToolOutputAt = 11560,

    }
}
