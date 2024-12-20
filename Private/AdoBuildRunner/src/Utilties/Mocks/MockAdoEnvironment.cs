// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.AdoBuildRunner.Utilties.Mocks
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

        /// <inheritdoc />
        public string AccessToken { get; set; }

        /// <nodoc />
        public MockAdoEnvironment()
        {
            BuildId = 12345;
            TeamProject = "MockTeamProject";
            TeamProjectId = "8e466b12-c3c1-4f66-a2f3-01b70ee74960";
            ServerUri = "https://dev.azure.com/mseng/";
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
            AccessToken = "MockAccessToken";
        }
    }
}