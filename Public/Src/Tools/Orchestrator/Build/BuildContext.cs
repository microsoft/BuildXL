// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Orchestrator.Build
{
    /// <summary>
    /// A build context represents an ongoing VSTS build and its most important properties
    /// </summary>
    public class BuildContext
    {
        /// <nodoc />
        public string BuildId { get; set; }

        /// <nodoc />
        public string SessionId { get; set; }

        /// <nodoc />
        public string SourcesDirectory { get; set; }

        /// <nodoc />
        public string ServerUrl { get; set; }

        /// <nodoc />
        public string RepositoryUrl { get; set; }

        /// <nodoc />
        public string TeamProjectId { get; set; }
    }
}
