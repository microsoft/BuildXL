// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.MsBuild.Serialization
{
    /// <summary>
    /// Arguments the graph builder tool should run with. Serialized into a JSON object from BuildXL and read by the graph builder tool.
    /// </summary>
    [JsonObject(IsReference = false)]
    public sealed class MSBuildGraphBuilderArguments
    {
        /// <summary>
        /// Root of the enlistment, required by BuildPrediction
        /// </summary>
        public string EnlistmentRoot { get; }

        /// <summary>
        /// Path to the entry point project to parse
        /// </summary>
        public IReadOnlyCollection<string> ProjectsToParse { get; }

        /// <summary>
        /// Path to the file were the graph will be serialized to
        /// </summary>
        public string OutputPath { get; }

        /// <summary>
        /// Global MSBuild properties to use for all projects
        /// </summary>
        /// <remarks>
        /// Can be null
        /// </remarks>
        [JsonProperty(IsReference = false)]
        public IReadOnlyDictionary<string, string> GlobalProperties { get; }

        /// <summary>
        /// Collection of paths to search for the MSBuild toolset (assemblies), and MSBuild.exe itself
        /// </summary>
        [JsonProperty(IsReference = false)]
        public IReadOnlyCollection<string> MSBuildSearchLocations { get; }

        /// <summary>
        /// Collection of target entry points.
        /// </summary>
        /// <remarks>
        /// Can be empty, in which case the default target of <see cref="ProjectsToParse"/> will be used
        /// </remarks>
        public IReadOnlyCollection<string> EntryPointTargets { get; }

        /// <nodoc/>
        public MSBuildGraphBuilderArguments(
            string enlistmentRoot,
            IReadOnlyCollection<string> projectsToParse, 
            string outputPath, 
            IReadOnlyDictionary<string, string> globalProperties, 
            IReadOnlyCollection<string> mSBuildSearchLocations, 
            IReadOnlyCollection<string> entryPointTargets)
        {
            Contract.Requires(!string.IsNullOrEmpty(enlistmentRoot));
            Contract.Requires(projectsToParse != null && projectsToParse.Count > 0);
            Contract.Requires(!string.IsNullOrEmpty(outputPath));
            Contract.Requires(mSBuildSearchLocations != null);
            Contract.Requires(entryPointTargets != null);

            EnlistmentRoot = enlistmentRoot;
            ProjectsToParse = projectsToParse;
            OutputPath = outputPath;
            GlobalProperties = globalProperties;
            MSBuildSearchLocations = mSBuildSearchLocations;
            EntryPointTargets = entryPointTargets;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return 
$@"Enlistment root: {EnlistmentRoot}
Project entry point: {string.Join(" ", ProjectsToParse)}
Serialized graph path: {OutputPath}
Global properties: {(GlobalProperties == null ? "null" : string.Join(" ", GlobalProperties.Select(kvp => $"[{kvp.Key}]={kvp.Value}")))}
Search locations: {string.Join(" ", MSBuildSearchLocations)}";
        }
    }
}
