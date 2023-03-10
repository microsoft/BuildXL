// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Processes.VmCommandProxy
{
    /// <summary>
    /// Input for 'InitializeVM' command.
    /// </summary>
    public class InitializeVmRequest
    {
        /// <summary>
        /// Subst drive.
        /// </summary>
        public string SubstDrive { get; set; }

        /// <summary>
        /// Subst path.
        /// </summary>
        public string SubstPath { get; set; }
    }

    /// <summary>
    /// Input for 'Run' command.
    /// </summary>
    public class RunRequest
    {
        /// <summary>
        /// Executable path.
        /// </summary>
        public string AbsolutePath { get; set; }

        /// <summary>
        /// Working directory.
        /// </summary>
        public string WorkingDirectory { get; set; }

        /// <summary>
        /// Arguments.
        /// </summary>
        public string Arguments { get; set; }
    }

    /// <summary>
    /// Output of 'Run' command.
    /// </summary>
    public class RunResult
    {
        /// <summary>
        /// Process state info.
        /// </summary>
        public ProcessStateInfo ProcessStateInfo { get; set; }

        /// <summary>
        /// Path to standard output.
        /// </summary>
        public string StdOut { get; set; }

        /// <summary>
        /// Path to standard error.
        /// </summary>
        public string StdErr { get; set; }
    }

    /// <summary>
    /// Process state info.
    /// </summary>
    public class ProcessStateInfo
    {
        /// <summary>
        /// Process state.
        /// </summary>
        public ProcessState ProcessState { get; set; }

        /// <summary>
        /// Exit code.
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// Last error.
        /// </summary>
        public string LastError { get; set; }

        /// <summary>
        /// Termination reason.
        /// </summary>
        public ProcessTerminationReason? TerminationReason { get; set; }

        /// <summary>
        /// StdOut path.
        /// </summary>
        public string StdOutPath { get; set; }

        /// <summary>
        /// StdErr path.
        /// </summary>
        public string StdErrPath { get; set; }
    }

    /// <summary>
    /// Process state.
    /// </summary>
    public enum ProcessState
    {
        /// <summary>
        /// Unknown.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Starting.
        /// </summary>
        Starting = 1,

        /// <summary>
        /// Startup error.
        /// </summary>
        StartupError = 2,

        /// <summary>
        /// Running.
        /// </summary>
        Running = 3,

        /// <summary>
        /// Exited.
        /// </summary>
        Exited = 4,

        /// <summary>
        /// Crash.
        /// </summary>
        Crashed = 5,

        /// <summary>
        /// Terminate error.
        /// </summary>
        TerminateError = 6,

        /// <summary>
        /// Terminated.
        /// </summary>
        Terminated = 7,

        /// <summary>
        /// Terminating.
        /// </summary>
        Terminating = 8,
    };

    /// <summary>
    /// Process termination reason.
    /// </summary>
    public enum ProcessTerminationReason
    {
        /// <summary>
        /// None.
        /// </summary>
        None = 0,

        /// <summary>
        /// Killed by client.
        /// </summary>
        KilledByClient = 1,

        /// <summary>
        /// Exceeded memory quota.
        /// </summary>
        ExceededMemoryQuota = 2,
    };
}
