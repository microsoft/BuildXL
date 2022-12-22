// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


namespace BuildXL.AdoBuildRunner.Build
{
    /// <summary>
    /// A build context represents an ongoing VSTS build and its most important properties
    /// </summary>
    public class BuildContext
    {
        /// <nodoc />
        public string BuildId { get; set; }

        /// <nodoc />
        public string RelatedSessionId { get; set; }

        /// <nodoc />
        public string SourcesDirectory { get; set; }

        /// <nodoc />
        public string ServerUrl { get; set; }

        /// <nodoc />
        public string RepositoryUrl { get; set; }

        /// <nodoc />
        public string TeamProjectId { get; set; }

        /// <summary>
        /// On a distributed build, a worker build triggered by the AdoBuildRunner
        /// will hold the GRPC endpoint to communicate with the orchestrator on this field,
        /// which will be null otherwise
        /// </summary>
        public string OrchestratorLocation { get; set; }

        /// <summary>
        /// On a distributed build, a worker build triggered by the AdoBuildRunner
        /// will hold the ADO Build Id of the triggering build.
        /// </summary>
        public string OrchestratorBuildId { get; set; }

    }
}
