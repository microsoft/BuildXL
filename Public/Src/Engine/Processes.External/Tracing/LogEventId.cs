// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Processes.External.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events />
    /// </summary>
    public enum LogEventId
    {
        PipProcessExternalExecution = 82,
        /// Sandboxed process remoting.
        FindAnyBuildClient = 12500, // was LogRemotingDebugMessage
        FindOrStartAnyBuildDaemon = 12501, // was LogRemotingErrorMessage
        ExceptionOnFindOrStartAnyBuildDaemon = 12504,
        ExceptionOnGetAnyBuildRemoteProcessFactory = 12505,
        InstallAnyBuildClient = 12506,
        FailedDownloadingAnyBuildClient = 12507,
        FailedInstallingAnyBuildClient = 12508,
        FinishedInstallAnyBuild = 12509,
        ExecuteAnyBuildBootstrapper = 12510,
        InstallAnyBuildClientDetails = 12511,
        ExceptionOnFindingAnyBuildClient = 12512,
        AnyBuildRepoConfigOverrides = 12513,
    }
}
