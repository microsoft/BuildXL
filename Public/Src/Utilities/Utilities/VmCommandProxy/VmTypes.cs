// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Newtonsoft.Json;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Input for 'StartBuild' command.
    /// </summary>
    /// <remarks>
    /// To be deprecated.
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public class StartBuildRequest
    {
    }

    /// <summary>
    /// Input for 'Run' command.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class RunRequest
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
    public class RunResult
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
    public class ProcessStateInfo
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
