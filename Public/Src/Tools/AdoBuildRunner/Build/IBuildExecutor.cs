// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.AdoBuildRunner.Build
{
    /// <summary>
    /// Defines the interface to execute a distributed build
    /// </summary>
    public interface IBuildExecutor
    {
        /// <summary>
        /// Sets up the build environment to execute the build
        /// </summary>
        /// <param name="buildContext">Information about the build setup</param>
        void PrepareBuildEnvironment(BuildContext buildContext);

        /// <summary>
        /// Execute a single machine build with a given context and arguments
        /// </summary>
        /// <param name="buildContext">Information about the build setup</param>
        /// <param name="buildArguments">Arguments to be executed when synchronization succeeds</param>
        /// <returns>Status code of the build argument execution</returns>
        int ExecuteSingleMachineBuild(BuildContext buildContext, string[] buildArguments);

        /// <summary>
        /// Execute a build with a given context and arguments as orchestrator
        /// </summary>
        /// <param name="buildContext">Information about the build setup</param>
        /// <param name="relatedSessionId">The related session id for this build</param>
        /// <param name="buildArguments">Arguments to be executed when synchronization succeeds</param>
        /// <returns>Status code of the build argument execution</returns>
        int ExecuteDistributedBuildAsOrchestrator(BuildContext buildContext, string relatedSessionId, string[] buildArguments);

        /// <summary>
        /// Perfrorm any work before setting the machine "ready" to build
        /// </summary>
        /// <param name="buildContext">The build context</param>
        /// <param name="buildArguments">Arguments to be executed when synchronization succeeds</param>
        void InitializeAsWorker(BuildContext buildContext, string[] buildArguments);

        /// <summary>
        ///  Execute a build with a given context and arguments as worker
        /// </summary>
        /// <param name="buildContext">The build context</param>
        /// <param name="buildInfo">The distributed build session information</param>
        /// <param name="buildArguments">Arguments to be executed when synchronization succeeds</param>
        /// <returns>Status code of the build argument execution</returns>
        int ExecuteDistributedBuildAsWorker(BuildContext buildContext, BuildInfo buildInfo, string[] buildArguments);
    }
}
