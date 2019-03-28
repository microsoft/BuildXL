// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Orchestrator.Build
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
        /// <param name="buildArguments">Arguments to be executed when orchestration succeeds</param>
        /// <returns>Status code of the build argument execution</returns>
        int ExecuteSingleMachineBuild(BuildContext buildContext, string[] buildArguments);

        /// <summary>
        /// Execute a build with a given context and arguments as orchestration master
        /// </summary>
        /// <param name="buildContext">Information about the build setup</param>
        /// <param name="buildArguments">Arguments to be executed when orchestration succeeds</param>
        /// <param name="workerInfo">An array of worker address information</param>
        /// <returns>Status code of the build argument execution</returns>
        int ExecuteDistributedBuildAsMaster(BuildContext buildContext, string[] buildArguments, List<IDictionary<string, string>> workerInfo);

        /// <summary>
        ///  Execute a build with a given context and arguments as orchestrated worker
        /// </summary>
        /// <param name="buildContext">The build context</param>
        /// <param name="buildArguments">Arguments to be executed when orchestration succeeds</param>
        /// <param name="masterInfo">The address information describing the build master</param>
        /// <returns>Status code of the build argument execution</returns>
        int ExecuteDistributedBuildAsWorker(BuildContext buildContext, string[] buildArguments, IDictionary<string, string> masterInfo);
    }
}
