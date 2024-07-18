// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace AdoBuildRunner
{
    /// <summary>
    /// Interface for accessing user defined config values required by ADOBuildRunner.
    /// </summary>
    public interface IAdoBuildRunnerConfig
    {
        /// <summary>
        /// Key used for invoking specific build runner logic
        /// </summary>
        public string AdoBuildRunnerInvocationKey { get; }

        /// <summary>
        /// PAT token used to authenticate with VSTS
        /// </summary>
        public string AccessToken { get; }

        /// <summary>
        /// Specifies the role of the pipeline in the ADO build runner
        /// </summary>
        public string AdoBuildRunnerPipelineRole { get; }

        /// <summary>
        /// Indicates whether the worker always reports success regardless of actual outcome
        /// </summary>
        public bool WorkerAlwaysSucceeds { get; }

        /// <summary>
        /// Maximum time to wait for a worker to be available, in seconds
        /// </summary>
        public int MaximumWaitForWorkerSeconds { get; }

        /// <summary>
        /// Disables encryption for the build process
        /// </summary>
        public bool DisableEncryption { get; }
    }
}
