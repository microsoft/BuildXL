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
        /// AdoBuildRunnerConfig object to access the user defined config values used by AdoBuildRunner.
        /// </summary>
        IAdoBuildRunnerConfig Config { get; }

        /// <summary>
        /// Gets the build context from the ADO build run information
        /// </summary>
        Task<BuildContext> GetBuildContextAsync(string buildKey);

        /// <summary>
        /// Wait until the orchestrator is ready and return its address
        /// </summary>
        Task<BuildInfo> WaitForBuildInfo(BuildContext buildContext);

        /// <summary>
        /// Publish the orchestrator address
        /// </summary>
        Task PublishBuildInfo(BuildContext buildContext, BuildInfo buildInfo);

        /// <summary>
        /// Retrieves role of the machine.
        /// </summary>
        MachineRole GetRole();

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