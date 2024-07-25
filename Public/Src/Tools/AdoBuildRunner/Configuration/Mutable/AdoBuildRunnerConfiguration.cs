// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using AdoBuildRunner;
using AdoBuildRunner.Vsts;
using BuildXL.AdoBuildRunner.Vsts;

namespace BuildXL.AdoBuildRunner
{
    /// <nodoc/>
    public sealed class AdoBuildRunnerConfiguration : IAdoBuildRunnerConfiguration
    {
        /// <inheritdoc />
        public string InvocationKey { get; set; }

        /// <inheritdoc />
        public MachineRole PipelineRole { get; set; }

        /// <inheritdoc />
        public bool WorkerAlwaysSucceeds { get; set; }

        /// <inheritdoc />
        public int MaximumWaitForWorkerSeconds { get; set; }

        /// <inheritdoc />
        public bool DisableEncryption { get; set; }

        /// <nodoc/>
        public CacheConfigGenerationConfiguration CacheConfigGenerationConfiguration { get; set; }

        /// <nodoc/>
        ICacheConfigGenerationConfiguration IAdoBuildRunnerConfiguration.CacheConfigGenerationConfiguration => CacheConfigGenerationConfiguration;

        /// <nodoc/>
        public AdoBuildRunnerConfiguration()
        {
            CacheConfigGenerationConfiguration = new CacheConfigGenerationConfiguration();
        }

        /// <summary>
        /// Set defaults values to ensure backwards compatibility until the changes are applied in the 1ESPT repo.
        /// See Bug: 2199401 for more details.
        /// </summary>
        public void PopulateFromEnvVars(IAdoEnvironment adoEnvironment, ILogger logger)
        {
            InvocationKey = Environment.GetEnvironmentVariable(Constants.InvocationKey) ?? string.Empty;
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

            PipelineRole = GetRole(adoEnvironment);
        }

        /// <summary>
        /// Retrieves role of the machine.
        /// </summary>
        private MachineRole GetRole(IAdoEnvironment adoEnvironment)
        {
            // For now, we explicitly mark the role with an environment
            // variable. When we discontinue the "non-worker-pipeline" approach, we can
            // infer the role from the parameters, but for now there is no easy way
            // to distinguish runs using the "worker-pipeline" model from ones who don't
            // When the build role is not specified, we assume this build is being run with the parallel strategy
            // where the role is inferred from the ordinal position in the phase: the first agent is the orchestrator
            var role = Environment.GetEnvironmentVariable(Constants.PipelineRole);

            if (string.IsNullOrEmpty(role) && adoEnvironment.JobPositionInPhase == 1)
            {
                return MachineRole.Orchestrator;
            }
            else if (Enum.TryParse(role, true, out MachineRole machineRole))
            {
                return machineRole;
            }
            else
            {
                throw new CoordinationException($"{Constants.PipelineRole} has an invalid value '{role}'. It should be '{MachineRole.Orchestrator}' or '{MachineRole.Worker}.");
            }
        }
    }
}