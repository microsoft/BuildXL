// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        /// Optional file paths for the projects or solutions that should be used to start parsing. These are relative
        /// paths with respect to the enlistment root.
        /// </summary>
        public IReadOnlyCollection<string> ProjectsToParse { get; }

        /// <summary>
        /// Path to the file were the graph will be serialized to
        /// </summary>
        public string OutputPath { get; }

        /// <summary>
        /// Global MSBuild properties to use for all projects
        /// </summary>
        [JsonProperty(IsReference = false)]
        public GlobalProperties GlobalProperties { get; }

        /// <summary>
        /// All requested qualifiers for this build, represented as global properties
        /// </summary>
        [JsonProperty(IsReference = false)]
        public IReadOnlyCollection<GlobalProperties> RequestedQualifiers { get; }

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

        /// <summary>
        /// Whether projects are allowed to not implement the target protocol
        /// </summary>
        /// <remarks>
        /// If true, default targets are used as heuristics
        /// </remarks>
        public bool AllowProjectsWithoutTargetProtocol { get; }

        /// <summary>
        /// Whether the MSBuild runtime is DotNet core (as opposed to full framework)
        /// </summary>
        public bool MsBuildRuntimeIsDotNetCore { get; }

        /// <summary>
        /// When true, MSBuild projects are not treated as first class citizens and MSBuild is instructed to build each project using the legacy mode,
        /// which relies on SDK conventions to respect the boundaries of a project and not build dependencies.
        /// </summary>
        public bool UseLegacyProjectIsolation { get; }

        /// <nodoc/>
        public MSBuildGraphBuilderArguments(
            IReadOnlyCollection<string> projectsToParse,
            string outputPath,
            GlobalProperties globalProperties,
            IReadOnlyCollection<string> mSBuildSearchLocations,
            IReadOnlyCollection<string> entryPointTargets,
            IReadOnlyCollection<GlobalProperties> requestedQualifiers,
            bool allowProjectsWithoutTargetProtocol,
            bool msBuildRuntimeIsDotNetCore,
            bool useLegacyProjectIsolation)
        {
            Contract.Requires(projectsToParse?.Count > 0);
            Contract.Requires(!string.IsNullOrEmpty(outputPath));
            Contract.Requires(globalProperties != null);
            Contract.Requires(mSBuildSearchLocations != null);
            Contract.Requires(entryPointTargets != null);
            Contract.Requires(requestedQualifiers?.Count > 0);

            ProjectsToParse = projectsToParse;
            OutputPath = outputPath;
            GlobalProperties = globalProperties;
            MSBuildSearchLocations = mSBuildSearchLocations;
            EntryPointTargets = entryPointTargets;
            RequestedQualifiers = requestedQualifiers;
            AllowProjectsWithoutTargetProtocol = allowProjectsWithoutTargetProtocol;
            MsBuildRuntimeIsDotNetCore = msBuildRuntimeIsDotNetCore;
            UseLegacyProjectIsolation = useLegacyProjectIsolation;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return
$@"Project entry points: [{string.Join(", ", ProjectsToParse)}]
Serialized graph path: {OutputPath}
Global properties: {string.Join(" ", GlobalProperties.Select(kvp => $"[{kvp.Key}]={kvp.Value}"))}
Search locations: {string.Join(" ", MSBuildSearchLocations)}
Requested qualifiers: {string.Join(" ", RequestedQualifiers.Select(qualifier => string.Join(";", qualifier.Select(kvp => $"[{kvp.Key}]={kvp.Value}"))))}
Allow projects without target protocol: {AllowProjectsWithoutTargetProtocol}
Use legacy project isolation: {UseLegacyProjectIsolation}
MSBuild runtime is DotNetCore: {MsBuildRuntimeIsDotNetCore}";
        }
    }
}
