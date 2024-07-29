// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using AdoBuildRunner;
using BuildXL.AdoBuildRunner.Build;

#nullable enable

namespace BuildXL.AdoBuildRunner.Vsts
{
    /// <summary>
    /// Defines the interactions with the VSTS API
    /// </summary>
    public interface IAdoBuildRunnerService
    {
        /// <summary>
        /// AdoEnvironment object to access the various env vars used by AdoBuildRunner.
        /// </summary>
        IAdoEnvironment AdoEnvironment { get; }

        /// <summary>
        /// IAdoBuildRunnerConfiguration object to access the user defined config values used by AdoBuildRunner.
        /// </summary>
        IAdoBuildRunnerConfiguration Config { get; }

        /// <summary>
        /// Gets the build context from the ADO build run information
        /// </summary>
        BuildContext BuildContext { get; }

        /// <summary>
        /// Wait until the orchestrator is ready and return its address
        /// </summary>
        Task<BuildInfo> WaitForBuildInfo();

        /// <summary>
        /// Publish the orchestrator address
        /// </summary>
        Task PublishBuildInfo( BuildInfo buildInfo);

        /// <summary>
        /// Gets a value associated to the key propertyName in the context of this build session
        /// </summary>
        Task<string?> GetBuildProperty(string propertyName);

        /// <summary>
        /// Sets a value associated to the key propertyName in the context of this build session
        /// </summary>
        Task PublishBuildProperty(string propertyName, string value);

        /// <summary>
        /// Retrieved the InvocationKey for invoking specific AdoBuildRunner logic.
        /// A build key has to be specified to disambiguate between multiple builds
        /// running as part of the same pipeline. This value is used to communicate
        /// the build information (orchestrator location, session id) to the workers. 
        /// </summary>
        string GetInvocationKey();

        /// <summary>
        /// Generates the CacheConfig file.
        /// </summary>
        Task GenerateCacheConfigFileIfNeededAsync(Logger logger, IAdoBuildRunnerConfiguration configuration, List<string> buildArgs);
    }
}