// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;

namespace BuildXL.AdoBuildRunner.Build
{
    /// <summary>
    /// A build context represents an ongoing VSTS build and its most important properties
    /// </summary>
    public record class BuildInfo
    {
        /// <nodoc />
        public required string RelatedSessionId { get; init; }

        /// <summary>
        /// On a distributed build, a worker build triggered by the AdoBuildRunner
        /// will hold the GRPC endpoint to communicate with the orchestrator on this field,
        /// which will be null otherwise
        /// </summary>
        public required string OrchestratorLocation { get; init; }

        /// <nodoc />
        public string Serialize() => $"{RelatedSessionId};{OrchestratorLocation}"; 

        /// <nodoc />
        public static BuildInfo Deserialize(string serialized)
        {
            var splits = serialized.Split(';');
            if (splits.Length != 2)
            {
                throw new ArgumentException("The provided string does not represent a valid BuildInfo");
            }

            return new BuildInfo { RelatedSessionId = splits[0], OrchestratorLocation = splits[1] };
        }
    }
}
