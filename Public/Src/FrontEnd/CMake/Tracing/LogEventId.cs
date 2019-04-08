// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.CMake.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
    #pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId
    {
        None = 0,

        // reserved 11600 .. 11700 CMake
        InvalidResolverSettings = 11600,
        ProjectRootDirectoryDoesNotExist = 11601,
        CMakeRunnerInternalError = 11602,
        CouldNotDeleteToolArgumentsFile = 11603,
        NoSearchLocationsSpecified = 11604,
        CannotParseBuildParameterPath = 11605,
    }
}
