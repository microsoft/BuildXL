// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace AdoBuildRunner
{
    /// <summary>
    /// Interface for the env vars required by the ADOBuildRunner.
    /// These vars are defined by the ADO environment.
    /// </summary>
    /// <remarks>
    /// All the predefined env vars needed for ADOBuildRunner should be added to this interface.
    /// </remarks>
    public interface IAdoEnvironment 
    {
        /// <summary>
        /// VSTS BuildId. This is unique per VSTS account
        /// </summary>
        public int BuildId { get; }

        /// <summary>
        /// Team project that the build definition belongs to
        /// </summary>
        public string TeamProject { get; }

        /// <summary>
        /// The id of the team project the build definition belongs to
        /// </summary>
        public string TeamProjectId { get; }

        /// <summary>
        /// Uri of the VSTS server that kicked off the build
        /// </summary>
        public string ServerUri { get; }

        /// <summary>
        /// Name of the Agent running the build
        /// </summary>
        public string AgentName { get; }

        /// <summary>
        /// Folder where the sources are being built from
        /// </summary>
        public string SourcesDirectory { get; }

        /// <summary>
        /// Id of the timeline of the build
        /// </summary>
        public string TimelineId { get; }

        /// <summary>
        /// Id of the plan of the build
        /// </summary>
        public string PlanId { get; }

        /// <summary>
        /// Url of the build repository
        /// </summary>
        public string RepositoryUrl { get; }

        /// <summary>
        /// Name of the machine where the agent is running
        /// </summary>
        public string AgentMachineName { get; }

        /// <summary>
        /// Unique identifier for the build job
        /// </summary>
        public string JobId { get; }

        /// <summary>
        /// URL of the collection where the build is being executed
        /// </summary>
        public string CollectionUrl { get; }

        /// <summary>
        /// Local branch of the source code being built
        /// </summary>
        public string LocalSourceBranch { get; }

        /// <summary>
        /// Local version of the source code being built
        /// </summary>
        public string LocalSourceVersion { get; }

        /// <summary>
        /// Indicates the job's position within its phase in the build process
        /// </summary>
        public int JobPositionInPhase { get; }

        /// <summary>
        /// Total number of jobs in the current phase of the build
        /// </summary>
        public int TotalJobsInPhase { get; }

        /// <summary>
        /// Variable indicating the current attempt number of the job
        /// </summary>
        public int JobAttemptNumber { get; }
    }
}