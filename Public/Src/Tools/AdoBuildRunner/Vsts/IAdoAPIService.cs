// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoBuildRunner
{
    /// <summary>
    /// Defines the methods necessary for interacting with Ado build services.
    /// This interface facilitates operations such as retrieving and updating build properties,
    /// accessing specific builds by their ID's, and obtaining information about the build that has been triggered.
    /// </summary>
    public interface IAdoAPIService
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
        /// Updates the build properties for a specified buildId.
        /// </summary>
        Task UpdateBuildPropertiesAsync(PropertiesCollection properties, int buildId);

        /// <summary>
        /// Gets the build trigger information.
        /// </summary>
        Task<Dictionary<string, string>> GetBuildTriggerInfoAsync();
    }
}
