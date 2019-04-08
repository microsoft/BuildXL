// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
