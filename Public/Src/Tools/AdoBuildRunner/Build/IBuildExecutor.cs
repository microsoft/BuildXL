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
        void PrepareBuildEnvironment();

        /// <summary>
        /// Launch a single machine build with a given context and arguments
        /// </summary>
        /// <param name="buildArguments">Arguments to be executed when synchronization succeeds</param>
        /// <returns>Status code of the build argument execution</returns>
        Task<int> ExecuteSingleMachineBuild(string[] buildArguments);

        /// <summary>
        /// Launch a build with a given context and arguments either as an orchstrator or a worker.
        /// </summary>
        /// <param name="buildArguments">Arguments to be executed when synchronization succeeds</param>
        Task<int> ExecuteDistributedBuild(string[] buildArguments);

        /// <summary>
        /// Define the arguments required by the build machine.
        /// </summary>
        /// <param name="buildInfo">The distributed build session information</param>
        /// <param name="buildArguments">Arguments to be executed when synchronization succeeds</param>
        /// <returns></returns>
        string[] ConstructArguments(BuildInfo buildInfo, string[] buildArguments);
    }
}
