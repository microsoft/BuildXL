// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.AdoBuildRunner
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

        /// <summary>
        /// The pool where the running agent was provided from.
        /// This can be different than the 'pool name' in the job definition
        /// if a backup pool is used. 
        /// </summary>
        public required string AgentPool { get; init; }

        /// <nodoc />
        public required int BuildId { get; init; }

        /// <nodoc />
        public required string AgentMachineName { get; init; }

        /// <nodoc />
        public required string AgentHostName { get; init; }

        /// <nodoc />
        public required string SourcesDirectory { get; init; }
    }
}
