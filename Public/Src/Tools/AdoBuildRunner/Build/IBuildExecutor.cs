// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;

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
        /// Execute a build with a given context and arguments either as an orchstrator or a worker.
        /// </summary>
        /// <param name="buildContext">Information about the build setup</param>
        /// <param name="buildArguments">Arguments to be executed when synchronization succeeds</param>
        Task<int> ExecuteDistributedBuild(BuildContext buildContext, string[] buildArguments);

        /// <summary>
        /// Define the arguments required by the build machine.
        /// </summary>
        /// <param name="buildContext">The build context</param>
        /// <param name="buildInfo">The distributed build session information</param>
        /// <param name="buildArguments">Arguments to be executed when synchronization succeeds</param>
        /// <returns></returns>
        string[] ConstructArguments(BuildContext buildContext, BuildInfo buildInfo, string[] buildArguments);
    }
}
