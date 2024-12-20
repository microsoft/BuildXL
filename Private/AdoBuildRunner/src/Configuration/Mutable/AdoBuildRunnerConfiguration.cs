// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.AdoBuildRunner.Vsts;

namespace BuildXL.AdoBuildRunner
{
    /// <nodoc/>
    public sealed class AdoBuildRunnerConfiguration : IAdoBuildRunnerConfiguration
    {
        /// <inheritdoc />
        public string EngineLocation { get; set; }

        /// <inheritdoc />
        public string InvocationKey { get; set; }

        /// <inheritdoc />
        public AgentRole? AgentRole { get; set; }

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
        public AdoBuildRunnerConfiguration(string engineLocation, string invocationKey)
        {
            EngineLocation = engineLocation;
            InvocationKey = invocationKey;
            CacheConfigGenerationConfiguration = new CacheConfigGenerationConfiguration();
        }

        /// <inheritdoc />
        public void PopulateFromEnvVars(IAdoEnvironment adoEnvironment, ILogger logger)
        {
            InvocationKey = Environment.GetEnvironmentVariable(Constants.InvocationKey) ?? InvocationKey;
            WorkerAlwaysSucceeds = WorkerAlwaysSucceeds || Environment.GetEnvironmentVariable(Constants.WorkerAlwaysSucceeds) == "true";
            DisableEncryption = DisableEncryption || Environment.GetEnvironmentVariable(Constants.DisableEncryptionVariableName) == "1";

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

            AgentRole = AgentRole ?? GetRole(adoEnvironment);
        }

        /// <summary>
        /// Retrieves role of the machine.
        /// </summary>
        private AgentRole GetRole(IAdoEnvironment adoEnvironment)
        {
            // For now, we explicitly mark the role with an environment
            // variable. When we discontinue the "non-worker-pipeline" approach, we can
            // infer the role from the parameters, but for now there is no easy way
            // to distinguish runs using the "worker-pipeline" model from ones who don't
            // When the build role is not specified, we assume this build is being run with the parallel strategy
            // where the role is inferred from the ordinal position in the phase: the first agent is the orchestrator
            var role = Environment.GetEnvironmentVariable(Constants.AgentRole);

            if (string.IsNullOrEmpty(role) && adoEnvironment.JobPositionInPhase == 1)
            {
                return AdoBuildRunner.AgentRole.Orchestrator;
            }
            else if (Enum.TryParse(role, true, out AgentRole agentRole))
            {
                return agentRole;
            }
            else
            {
                throw new CoordinationException($"{Constants.AgentRole} has an invalid value '{role}'. It should be '{AdoBuildRunner.AgentRole.Orchestrator}' or '{AdoBuildRunner.AgentRole.Worker}.");
            }
        }
    }
}