// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner.Vsts;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

namespace BuildXL.AdoBuildRunner
{
    /// <summary>
    /// Defines the methods necessary for interacting with Ado build services.
    /// This interface facilitates operations such as retrieving and updating build properties,
    /// accessing specific builds by their ID's, and obtaining information about the build that has been triggered.
    /// </summary>
    public interface IAdoService
    {
        /// <summary>
        /// Gets the build properties for a specificied buildId.
        /// </summary>
        Task<PropertiesCollection> GetBuildPropertiesAsync(int buildId);

        /// <summary>
        /// Gets a build for a specified buildId.
        /// </summary>
        Task<Build> GetBuildAsync(int buildId);

        /// <summary>
        /// Returns the terminal state of the orchestrator's ADO job within the given build's timeline.
        /// Returns <see cref="OrchestratorState.Running"/> while the job is still in progress, when its
        /// record is not yet in the timeline, or when it completed successfully.
        /// </summary>
        /// <remarks>
        /// Watches the orchestrator's JOB record in the build timeline (not the overall build's
        /// status/result). CODESYNC: <see cref="BuildInfo.OrchestratorJobId"/>.
        /// </remarks>
        Task<OrchestratorState> GetOrchestratorStateAsync(int buildId, Guid orchestratorJobId);

        /// <summary>
        /// Updates the build properties for a specified buildId.
        /// </summary>
        Task UpdateBuildPropertiesAsync(PropertiesCollection properties, int buildId);

        /// <summary>
        /// Gets the build trigger information.
        /// </summary>
        Task<Dictionary<string, string>> GetBuildTriggerInfoAsync();

        /// <summary>
        /// Gets the name of the pool this agent is running on
        /// </summary>
        /// <returns></returns>
        Task<string> GetPoolNameAsync();
    }
}
