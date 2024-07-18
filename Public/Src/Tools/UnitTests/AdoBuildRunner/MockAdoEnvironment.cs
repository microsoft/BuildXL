// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Reflection;
using AdoBuildRunner;

namespace Test.Tool.AdoBuildRunner
{
    /// <summary>
    /// Mock implementation of IAdoEnvironment for testing purpose.
    /// </summary>
    public class MockAdoEnvironment : IAdoEnvironment
    {
        /// <inheritdoc />
        public int BuildId { get; set; }

        /// <inheritdoc />
        public string TeamProject { get; set; }

        /// <inheritdoc />
        public string TeamProjectId { get; set; }

        /// <inheritdoc />
        public string ServerUri { get; set; }

        /// <inheritdoc />
        public string AgentName { get; set; }

        /// <inheritdoc />
        public string SourcesDirectory { get; set; }

        /// <inheritdoc />
        public string TimelineId { get; set; }

        /// <inheritdoc />
        public string PlanId { get; set; }

        /// <inheritdoc />
        public string RepositoryUrl { get; set; }

        /// <inheritdoc />
        public string AgentMachineName { get; set; }

        /// <inheritdoc />
        public string JobId { get; set; }

        /// <inheritdoc />
        public string CollectionUrl { get; set; }

        /// <inheritdoc />
        public string MaximumWaitForWorkerSecondsVariableName { get; set; }

        /// <inheritdoc />
        public string LocalSourceBranch { get; set; }

        /// <inheritdoc />
        public string LocalSourceVersion { get; set; }

        /// <inheritdoc />
        public int JobPositionInPhase { get; set; }

        /// <inheritdoc />
        public int TotalJobsInPhase { get; set; }

        /// <inheritdoc />
        public int JobAttemptNumber { get; set; }

        /// <nodoc />
        public MockAdoEnvironment()
        {
            BuildId = 12345;
            TeamProject = "MockTeamProject";
            TeamProjectId = "MockTeamProjectId";
            ServerUri = "MockServerUri";
            AgentName = "MockAgentName";
            AgentMachineName = "MockAgentMachineName";
            SourcesDirectory = "MockSourcesDirectory";
            TimelineId = "MockTimelineId";
            JobId = "234";
            PlanId = "MockPlanId";
            RepositoryUrl = "MockRepositoryUrl";
            CollectionUrl = "MockCollectionUrl";
            MaximumWaitForWorkerSecondsVariableName = "1000";
            LocalSourceBranch = "MockLocalSourceBranch";
            LocalSourceVersion = "MockLocalSourceVersion";
            JobAttemptNumber = 1;
            JobPositionInPhase = 1;
            TotalJobsInPhase = 1;
        }
    }
}