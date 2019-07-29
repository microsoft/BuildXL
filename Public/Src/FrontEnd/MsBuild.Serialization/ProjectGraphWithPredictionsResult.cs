// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.MsBuild.Serialization
{
    /// <summary>
    /// Represents either a successfully created ProjectGraphWithPredictions or a failed one with an associated reason
    /// </summary>
    [JsonObject(IsReference = false)]
    public readonly struct ProjectGraphWithPredictionsResult<TPathType>
    {
        /// <nodoc/>
        public ProjectGraphWithPredictions<TPathType> Result { get; }

        /// <nodoc/>
        public GraphConstructionError Failure { get; }

        /// <summary>
        /// Paths of the required MsBuild assemblies that were found
        /// </summary>
        /// <remarks>
        /// Informative only, mainly for debugging purposes
        /// </remarks>
        [JsonProperty(IsReference = false)]
        public IReadOnlyDictionary<string, TPathType> MsBuildAssemblyPaths { get; }
        
        /// <summary>
        /// Path to MsBuild that was found
        /// </summary>
        /// <remarks>
        /// The path may not be valid if <see cref="Succeeded"/> is false.
        /// The last component of the path could be either MSBuild.exe or MSBuild.dll, depending on the selected runtime.
        /// </remarks>
        public TPathType PathToMsBuild { get; }

        /// <summary>
        /// Path to dotnet.exe, if the dotnet core version of MSBuild was specified to run
        /// </summary>
        /// <remarks>
        /// This is not actually populated by the graph construction tool, but by bxl
        /// </remarks>
        public TPathType PathToDotNetExe { get; }

        /// <nodoc/>
        public bool Succeeded { get; }

        /// <nodoc/>
        public static ProjectGraphWithPredictionsResult<TPathType> CreateSuccessfulGraph(ProjectGraphWithPredictions<TPathType> projectGraphWithPredictions, IReadOnlyDictionary<string, TPathType> assemblyPathsToLoad, TPathType pathToMsBuild)
        {
            Contract.Requires(projectGraphWithPredictions != null);
            Contract.Requires(assemblyPathsToLoad != null);
            return new ProjectGraphWithPredictionsResult<TPathType>(projectGraphWithPredictions, failure: default, msBuildAssemblyPaths: assemblyPathsToLoad, pathToMsBuild: pathToMsBuild, pathToDotNetExe: default(TPathType), succeeded: true);
        }

        /// <nodoc/>
        public static ProjectGraphWithPredictionsResult<TPathType> CreateFailure(GraphConstructionError failure, IReadOnlyDictionary<string, TPathType> assemblyPathsToLoad, TPathType pathToMsBuild)
        {
            Contract.Requires(failure != null);
            Contract.Requires(assemblyPathsToLoad != null);
            return new ProjectGraphWithPredictionsResult<TPathType>(default, failure, assemblyPathsToLoad, pathToMsBuild, pathToDotNetExe: default(TPathType), succeeded: false);
        }

        /// <summary>
        /// Returns a new instance of this with a specific path to dotnet.exe
        /// </summary>
        public ProjectGraphWithPredictionsResult<TPathType> WithPathToDotNetExe(TPathType pathToDotNetExe)
        {
            return new ProjectGraphWithPredictionsResult<TPathType>(Result, Failure, MsBuildAssemblyPaths, PathToMsBuild, pathToDotNetExe, Succeeded);
        }

        [JsonConstructor]
        private ProjectGraphWithPredictionsResult(ProjectGraphWithPredictions<TPathType> result, GraphConstructionError failure, IReadOnlyDictionary<string, TPathType> msBuildAssemblyPaths, TPathType pathToMsBuild, TPathType pathToDotNetExe, bool succeeded)
        {
            Result = result;
            Failure = failure;
            Succeeded = succeeded;
            PathToMsBuild = pathToMsBuild;
            PathToDotNetExe = pathToDotNetExe;
            MsBuildAssemblyPaths = msBuildAssemblyPaths;
        }
    }
}
