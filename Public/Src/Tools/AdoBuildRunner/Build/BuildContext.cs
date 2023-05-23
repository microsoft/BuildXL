// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;

#nullable enable

namespace BuildXL.AdoBuildRunner.Build
{
    /// <summary>
    /// A build context represents an ongoing VSTS build and its most important properties
    /// </summary>
    public record BuildContext
    {
        /// <summary>
        /// A key that is unique to a build runner invocation within a pipeline. 
        /// Used to disambiguate between different build sessions that occur in the same pipeline run.
        /// </summary>
        public required string InvocationKey { get; init; }

        /// <nodoc />
        public required DateTime StartTime { get; init; }

        /// <nodoc />
        public required string BuildId { get; init; }

        /// <nodoc />
        public required string AgentMachineName { get; init; }

        /// <nodoc />
        public required string SourcesDirectory { get; init; }

        /// <nodoc />
        public required string ServerUrl { get; init; }

        /// <nodoc />
        public required string RepositoryUrl { get; init; }

        /// <nodoc />
        public required string TeamProjectId { get; init; }
    }
}
