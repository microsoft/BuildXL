// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Specifies roles that nodes can serve as in a distributed build (if applicable)
    /// </summary>
    [Flags]
    public enum DistributedBuildRoles : byte
    {
        /// <summary>
        /// Not running distributed build.
        /// </summary>
        None = 0,

        /// <summary>
        /// Specifies that the current node acts as the build coordinator
        /// </summary>
        /// <remarks>
        /// To be replaced with 'Orchestrator'
        /// </remarks>
        Master = 1,

        /// <summary>
        /// Specifies that the current node acts as a build worker
        /// </summary>
        Worker = 2,

        /// <summary>
        /// Specifies that the current node acts as a build coordinator
        /// </summary>
        Orchestrator = 3,
    }

    /// <nodoc />
    /// <remarks>
    /// To be removed when we remove the 'Master' role
    /// </remarks>
    public static class DistributedBuildRolesExtensions
    {
        /// <summary>
        /// Helper method to check if the role is orchestrator while keeping the 'Master' role for compatibility
        /// </summary>
        public static bool IsOrchestrator(this DistributedBuildRoles role)
        {
            return role == DistributedBuildRoles.Master || role == DistributedBuildRoles.Orchestrator;
        } 
    }
}

