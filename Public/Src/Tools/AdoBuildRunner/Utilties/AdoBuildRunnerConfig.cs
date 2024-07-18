// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.AdoBuildRunner;
using BuildXL.AdoBuildRunner.Vsts;

namespace AdoBuildRunner
{
    /// <summary>
    /// Implementation of IAdoBuildRunnerConfig that provides user defined config values.
    /// </summary>
    public class AdoBuildRunnerConfig : IAdoBuildRunnerConfig
    {
        /// <inheritdoc />
        public string AccessToken { get; }

        /// <inheritdoc />
        public string AdoBuildRunnerPipelineRole { get; }

        /// <inheritdoc />
        public string AdoBuildRunnerInvocationKey { get; }

        /// <inheritdoc />
        public bool DisableEncryption { get; }

        /// <inheritdoc />
        public bool WorkerAlwaysSucceeds { get; }

        /// <inheritdoc />
        public int MaximumWaitForWorkerSeconds { get; }

        /// <nodoc />
        public AdoBuildRunnerConfig() { }

        /// <nodoc />
        public AdoBuildRunnerConfig(ILogger logger)
        {
            AccessToken = Environment.GetEnvironmentVariable(Constants.AccessTokenVarName)!;
            AdoBuildRunnerPipelineRole = Environment.GetEnvironmentVariable(Constants.AdoBuildRunnerPipelineRole)!;
            AdoBuildRunnerInvocationKey = Environment.GetEnvironmentVariable(Constants.AdoBuildRunnerInvocationKey)!;

            WorkerAlwaysSucceeds = Environment.GetEnvironmentVariable(Constants.WorkerAlwaysSucceeds) == "true";
            DisableEncryption = Environment.GetEnvironmentVariable(Constants.DisableEncryptionVariableName) == "1";

            MaximumWaitForWorkerSeconds = Constants.DefaultMaximumWaitForWorkerSeconds;
            var userMaxWaitingTime = Environment.GetEnvironmentVariable(Constants.MaximumWaitForWorkerSecondsVariableName);
            if (!string.IsNullOrEmpty(userMaxWaitingTime))
            {
                if (!int.TryParse(userMaxWaitingTime, out var maxWaitingTime))
                {
                    logger.Warning($"Couldn't parse value '{userMaxWaitingTime}' for {Constants.MaximumWaitForWorkerSecondsVariableName}." +
                        $"Using the default value of {Constants.DefaultMaximumWaitForWorkerSeconds}");
                }
                else
                {
                    MaximumWaitForWorkerSeconds = maxWaitingTime;
                }
            }
        }
    }
}
