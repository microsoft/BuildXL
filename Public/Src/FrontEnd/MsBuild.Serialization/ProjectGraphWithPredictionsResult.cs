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
        /// Path to MsBuild.exe that was found
        /// </summary>
        /// <remarks>
        /// The path may not be valid if <see cref="Succeeded"/> is false.
        /// </remarks>
        public TPathType PathToMsBuildExe { get; }

        /// <nodoc/>
        public bool Succeeded { get; }

        /// <nodoc/>
        public static ProjectGraphWithPredictionsResult<TPathType> CreateSuccessfulGraph(ProjectGraphWithPredictions<TPathType> projectGraphWithPredictions, IReadOnlyDictionary<string, TPathType> assemblyPathsToLoad, TPathType pathToMsBuildExe)
        {
            Contract.Requires(projectGraphWithPredictions != null);
            Contract.Requires(assemblyPathsToLoad != null);
            return new ProjectGraphWithPredictionsResult<TPathType>(projectGraphWithPredictions, failure: default, msBuildAssemblyPaths: assemblyPathsToLoad, pathToMsBuildExe: pathToMsBuildExe, succeeded: true);
        }

        /// <nodoc/>
        public static ProjectGraphWithPredictionsResult<TPathType> CreateFailure(GraphConstructionError failure, IReadOnlyDictionary<string, TPathType> assemblyPathsToLoad, TPathType pathToMsBuildExe)
        {
            Contract.Requires(failure != null);
            Contract.Requires(assemblyPathsToLoad != null);
            return new ProjectGraphWithPredictionsResult<TPathType>(default, failure, assemblyPathsToLoad, pathToMsBuildExe, succeeded: false);
        }

        [JsonConstructor]
        private ProjectGraphWithPredictionsResult(ProjectGraphWithPredictions<TPathType> result, GraphConstructionError failure, IReadOnlyDictionary<string, TPathType> msBuildAssemblyPaths, TPathType pathToMsBuildExe, bool succeeded)
        {
            Result = result;
            Failure = failure;
            Succeeded = succeeded;
            PathToMsBuildExe = pathToMsBuildExe;
            MsBuildAssemblyPaths = msBuildAssemblyPaths;
        }
    }
}
