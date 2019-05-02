// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Newtonsoft.Json;

namespace BuildXL.Processes.VmProxy
{
    /// <summary>
    /// Input for 'StartBuild' command.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    internal class StartBuildRequest
    {
        /// <summary>
        /// User name.
        /// </summary>
        [JsonProperty]
        public string HostLowPrivilegeUsername { get; set; }

        /// <summary>
        /// Password.
        /// </summary>
        [JsonProperty]
        public string HostLowPrivilegePassword { get; set; }
    }

    /// <summary>
    /// Input for 'Run' command.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    internal class RunRequest
    {
        /// <summary>
        /// Executable path.
        /// </summary>
        [JsonProperty]
        public string AbsolutePath { get; set; }

        /// <summary>
        /// Working directory.
        /// </summary>
        [JsonProperty]
        public string WorkingDirectory { get; set; }

        /// <summary>
        /// Arguments.
        /// </summary>
        [JsonProperty]
        public string Arguments { get; set; }
    }

    /// <summary>
    /// Output of 'Run' command.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    internal class RunResult
    {
        /// <summary>
        /// Process state info.
        /// </summary>
        [JsonProperty]
        public ProcessStateInfo ProcessStateInfo { get; set; }

        /// <summary>
        /// Path to standard output.
        /// </summary>
        [JsonProperty]
        public string StdOut { get; set; }

        /// <summary>
        /// Path to standard error.
        /// </summary>
        [JsonProperty]
        public string StdErr { get; set; }
    }

    /// <summary>
    /// Process state info.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    internal class ProcessStateInfo
    {
        /// <summary>
        /// Process state.
        /// </summary>
        [JsonProperty]
        public ProcessState ProcessState { get; set; }

        /// <summary>
        /// Exit code.
        /// </summary>
        [JsonProperty]
        public int ExitCode { get; set; }

        /// <summary>
        /// Last error.
        /// </summary>
        [JsonProperty]
        public string LastError { get; set; }

        /// <summary>
        /// Termination reason.
        /// </summary>
        [JsonProperty]
        public ProcessTerminationReason? TerminationReason { get; set; }

        /// <summary>
        /// StdOut path.
        /// </summary>
        [JsonProperty]
        public string StdOutPath { get; set; }

        /// <summary>
        /// StdErr path.
        /// </summary>
        [JsonProperty]
        public string StdErrPath { get; set; }
    }

    /// <summary>
    /// Process state.
    /// </summary>
    internal enum ProcessState
    {
        Unknown = 0,
        Starting = 1,
        StartupError = 2,
        Running = 3,
        Exited = 4,
        Crashed = 5,
        TerminateError = 6,
        Terminated = 7,
        Terminating = 8,
    };

    /// <summary>
    /// Process termination reason.
    /// </summary>
    internal enum ProcessTerminationReason
    {
        None = 0,
        KilledByClient = 1,
        ExceededMemoryQuota = 2,
    };
}
