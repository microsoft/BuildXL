// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration.Resolvers;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for MSBuild resolver
    /// </summary>
    public interface IMsBuildResolverSettings : IProjectGraphResolverSettings, IUntrackingSettings
    {
        /// <summary>
        /// The directory where the resolver starts parsing the enlistment
        /// (including all sub-directories recursively). Not necessarily the
        /// same as <see cref="IProjectGraphResolverSettings.Root"/> for cases where the codebase to process
        /// starts in a subdirectory of the enlistment.
        /// </summary>
        /// <remarks>
        /// If this is not specified, it will default to <see cref="IProjectGraphResolverSettings.Root"/>
        /// </remarks>
        AbsolutePath RootTraversal { get; }

        /// <summary>
        /// Output directories to be added in addition to the ones BuildXL predicts
        /// </summary>
        IReadOnlyList<DirectoryArtifact> AdditionalOutputDirectories { get; }

        /// <summary>
        /// Whether pips scheduled by this resolver should run in an isolated container.
        /// </summary>
        bool RunInContainer { get; }

        /// <summary>
        /// Collection of directories to search for the required MsBuild assemblies and MsBuild.exe.
        /// </summary>
        /// <remarks>
        /// If this is not defined, locations in %PATH% are used
        /// Locations are traversed in order
        /// </remarks>
        IReadOnlyList<DirectoryArtifact> MsBuildSearchLocations { get; }

        /// <summary>
        /// Whether to use the full framework or dotnet core version of MSBuild. 
        /// </summary>
        /// <remarks>
        /// Selected runtime is used both for build evaluation and execution.
        /// Default is full framework.
        /// Observe that using the full framework version means that msbuild.exe is expected to be found in msbuildSearchLocations
        /// (or PATH if not specified). If using the dotnet core version, the same logic applies but to msbuild.dll
        /// </remarks>
        string MsBuildRuntime { get; }

        /// <summary>
        /// Collection of directories to search for dotnet.exe, when DotNetCore is specified as the msBuildRuntime. 
        /// </summary>
        /// <remarks>
        /// If not specified, locations in %PATH% are used.
        /// Locations are traversed in specification order.
        /// </remarks>
        IReadOnlyList<DirectoryArtifact> DotNetSearchLocations { get; }

        /// <summary>
        /// Optional file paths for the projects or solutions that should be used to start parsing. These are relative 
        /// paths with respect to the root traversal.
        /// </summary>
        /// <remarks>
        /// If not provided, BuildXL will attempt to find a candidate under the root traversal. If more than one 
        /// candidate is available, the process will fail.
        /// </remarks>
        IReadOnlyList<RelativePath> FileNameEntryPoints { get; }

        /// <summary>
        /// Targets to execute on the entry point project.
        /// </summary>
        /// <remarks>
        /// If not provided, the default targets are used.
        /// Initial targets are mapped to /target (or /t) when invoking MSBuild for the entry point project.
        /// </remarks>
        IReadOnlyList<string> InitialTargets { get; }

        /// <summary>
        /// Global properties to use for all projects.
        /// </summary>
        IReadOnlyDictionary<string, string> GlobalProperties { get; }

        /// <summary>
        /// Allows requesting additional MSBuild log output.
        /// When "none" or null, no additional MSBuild logging is performed besides normal level console
        /// output. Otherwise an additional text log 'msbuild.log' is generated for
        /// MSBuild projects, alongside other log files, with the requested verbosity.
        /// Typically the value requested here is 'diag' or 'diagnostic'
        /// to get a lot of (expensive) logging.
        /// </summary>
        string LogVerbosity { get; }

        /// <summary>
        /// When true, an msbuild.binlog is generated for each MSBuild project alongside
        /// any existing logs.
        /// </summary>
        bool? EnableBinLogTracing { get; }

        /// <summary>
        /// When true, additional engine trace outputs are requested from MSBuild.
        /// </summary>
        bool? EnableEngineTracing { get; }

        /// <summary>
        /// Whether each project has implicit access to the transitive closure of its references. 
        /// </summary>
        /// <remarks>
        /// Turning this option on may imply a decrease in build performance, but many existing MSBuild repos rely on an equivalent feature.
        /// Defaults to false.
        /// </remarks>
        bool? EnableTransitiveProjectReferences { get; }

        /// <summary>
        /// When true, MSBuild projects are not treated as first class citizens and MSBuild is instructed to build each project using the legacy mode, 
        /// which relies on SDK conventions to respect the boundaries of a project and not build dependencies. 
        /// </summary>
        /// <remarks>
        /// The legacy mode is less restrictive than the
        /// default mode, where explicit project references to represent project dependencies are strictly enforced, but a decrease in build performance and
        /// other build failures may occur (e.g. double writes due to overbuilds).
        /// Defaults to false.
        /// </remarks>
        bool?  UseLegacyProjectIsolation { get; }

        /// <summary>
        /// Policy to apply when a double write occurs.
        /// </summary>
        /// <remarks>
        /// By default double writes are only allowed if the produced content is the same.
        /// </remarks>
        DoubleWritePolicy? DoubleWritePolicy { get; }

        /// <summary>
        /// Whether projects are allowed to not specify their target protocol.
        /// </summary>
        /// <remarks>
        /// When true, default targets will be used as a heuristic. Defaults to false.
        /// </remarks>
        bool? AllowProjectsToNotSpecifyTargetProtocol { get; }

        /// <summary>
        /// Whether VBCSCompiler is allowed to be launched as a service to serve managed compilation requests
        /// </summary>
        /// <remarks>
        /// Defaults to on.
        /// This option will only be honored when process breakaway is supported by the underlying sandbox.
        /// </remarks>
        bool? UseManagedSharedCompilation { get; }
    }

    /// <nodoc/>
    public static class MsBuildResolverSettingsExtensions
    {
        /// <summary>
        /// Whether MSBuildRuntime is DotNetCore.
        /// </summary>
        /// <remarks>
        /// Keep in sync with Public\Sdk\Public\Prelude\Prelude.Configuration.Resolvers.dsc
        /// If not specified, the default is full framework, so this function returns false in that case.
        /// </remarks>
        public static bool ShouldRunDotNetCoreMSBuild(this IMsBuildResolverSettings msBuildResolverSettings) => msBuildResolverSettings.MsBuildRuntime == "DotNetCore";
    }
}
