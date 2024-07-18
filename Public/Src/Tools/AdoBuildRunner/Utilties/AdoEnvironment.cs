// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.AdoBuildRunner;
using BuildXL.AdoBuildRunner.Vsts;
using AdoBuildRunner.Vsts;

namespace AdoBuildRunner
{
    /// <summary>
    /// Implementation of IAdoEnvironment that provides env vars required by ADOBuildRunner.
    /// This class retrieves the env vars used during the ADO build process.
    /// </summary>
    public class AdoEnvironment : IAdoEnvironment
    {
        /// <inheritdoc />
        public int BuildId { get; }

        /// <inheritdoc />
        public string TeamProject { get; }

        /// <inheritdoc />
        public string TeamProjectId { get; }

        /// <inheritdoc />
        public string ServerUri { get; }

        /// <inheritdoc />
        public string AgentName { get; }

        /// <inheritdoc />
        public string SourcesDirectory { get; }

        /// <inheritdoc />
        public string TimelineId { get; }

        /// <inheritdoc />
        public string PlanId { get; }

        /// <inheritdoc />
        public string RepositoryUrl { get; }

        /// <inheritdoc />
        public string AgentMachineName { get; }

        /// <inheritdoc />
        public string JobId { get; }

        /// <inheritdoc />
        public string CollectionUrl { get; }

        /// <inheritdoc />
        public string MaximumWaitForWorkerSeconds { get; }

        /// <inheritdoc />
        public string LocalSourceBranch {  get; }

        /// <inheritdoc />
        public string LocalSourceVersion { get; }

        /// <inheritdoc />
        public int JobPositionInPhase { get; }

        /// <inheritdoc />
        public int TotalJobsInPhase { get; }

        /// <inheritdoc />
        public int JobAttemptNumber { get; }

        /// <nodoc />
        public AdoEnvironment() { }

        /// <nodoc />
        public AdoEnvironment(ILogger logger) 
        {
            // See: https://learn.microsoft.com/en-us/azure/devops/pipelines/build/variables?view=azure-devops&tabs=yaml
            BuildId = int.Parse(Environment.GetEnvironmentVariable(Constants.BuildIdVarName));
            TeamProject = Environment.GetEnvironmentVariable(Constants.TeamProjectVarName)!;
            ServerUri = Environment.GetEnvironmentVariable(Constants.ServerUriVarName)!;
            AgentName = Environment.GetEnvironmentVariable(Constants.AgentNameVarName)!;
            AgentMachineName = Environment.GetEnvironmentVariable(Constants.AgentMachineNameVarName)!;
            SourcesDirectory = Environment.GetEnvironmentVariable(Constants.SourcesDirectoryVarName)!;
            TeamProjectId = Environment.GetEnvironmentVariable(Constants.TeamProjectIdVarName)!;
            TimelineId = Environment.GetEnvironmentVariable(Constants.TimelineIdVarName)!;
            JobId = Environment.GetEnvironmentVariable(Constants.JobIdVariableName)!;
            PlanId = Environment.GetEnvironmentVariable(Constants.PlanIdVarName)!;
            RepositoryUrl = Environment.GetEnvironmentVariable(Constants.RepositoryUrlVariableName)!;
            CollectionUrl = Environment.GetEnvironmentVariable(Constants.CollectionUrlVariableName)!;
            MaximumWaitForWorkerSeconds = Environment.GetEnvironmentVariable(Constants.MaximumWaitForWorkerSecondsVariableName)!;
            LocalSourceBranch = Environment.GetEnvironmentVariable(Constants.SourceBranchVariableName)!;
            LocalSourceVersion = Environment.GetEnvironmentVariable(Constants.SourceVersionVariableName)!;

            var jobAttemptNumber = Environment.GetEnvironmentVariable(Constants.JobAttemptVariableName);
            JobAttemptNumber = string.IsNullOrEmpty(jobAttemptNumber) ? 1: int.Parse(jobAttemptNumber);

            var jobPositionInPhase = Environment.GetEnvironmentVariable(Constants.JobsPositionInPhaseVarName)!;
            var totalJobsInPhase = Environment.GetEnvironmentVariable(Constants.TotalJobsInPhaseVarName)!;

            if (string.IsNullOrWhiteSpace(jobPositionInPhase))
            {
                logger.Info("The job position in the build phase could not be determined. Therefore it must be a single machine build");
                JobPositionInPhase = 1;
                TotalJobsInPhase = 1;
            }
            else
            {
                if (!int.TryParse(jobPositionInPhase, out int position))
                {
                    CoordinationException.LogAndThrow(logger, $"The env var {Constants.JobsPositionInPhaseVarName} contains a value that cannot be parsed to int");
                }

                JobPositionInPhase = position;

                if (!int.TryParse(totalJobsInPhase, out int totalJobs))
                {
                    CoordinationException.LogAndThrow(logger, $"The env var {Constants.TotalJobsInPhaseVarName} contains a value that cannot be parsed to int");
                }

                TotalJobsInPhase = totalJobs;
            }
        }
    }
}
