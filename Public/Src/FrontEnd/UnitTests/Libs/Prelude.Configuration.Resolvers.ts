// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <reference path="Prelude.Core.dsc"/>
/// <reference path="Prelude.IO.dsc"/>

/**
 * Source resolver that uses specified source paths for module resolution.
 */
interface DScriptResolver {
    // Should be "SourceResolver"
    kind: "DScript" | "SourceResolver";

    /** Root directory where packages are stored. */
    root?: Directory;

    /** List of packages with respecting path where to look for this package.
     * Obsolete, use 'modules' instead
    */
    //@@obsolete
    packages?: File[];

    /** List of modules with respecting path where to look for this module. */
    modules?: File[];

    /** Weather specs under this resolver's root should be evaluated as part of the build. */
    definesBuildExtent?: boolean;
}

/**
 * Custom resolver that uses NuGet for getting packages.
 */
interface NuGetResolver {
    kind: "Nuget";

    /**
     * Optional configuration to fix the version of nuget to use.
     *  When not specified the latest one will be used.
     */
    configuration?: NuGetConfiguration;

    /**
     * The list of respositories to use to resolve. Keys are the name, values are the urls
     */
    repositories?: { [name: string]: string; };

    /**
     * The transitive set of NuGet packages to retrieve
     */
    packages?: {id: string; version: string; alias?: string; tfm?: string; dependentPackageIdsToSkip?: string[]; dependentPackageIdsToIgnore?: string[], forceFullFrameworkQualifiersOnly?: boolean}[];

    /**
     * Whether to enforce that the version range specified for dependencies in a NuGet package
     * match the package version specified in the configuration file.
     * This is enforced if not specified */
    doNotEnforceDependencyVersions?: boolean;
}

// We represent a passthrough environment variable with unit
type PassthroughEnvironmentVariable = Unit;

/**
 * Resolver for MSBuild project-level build execution, utilizing the MsBuild static graph API to
 * find MSBuild files and convert them to a pip graph
 */
interface MsBuildResolver {
    kind: "MsBuild";

    /**
     * The enlistment root. This may not be the location where parsing should begin;
     * 'rootTraversal' can override that behavior.
     */
    root: Directory;

    /**
     * The name of the module exposed to other DScript projects that will include all MSBuild projects found under
     * the enlistment
     */
    moduleName: string;

    /**
     * The directory where the resolver starts parsing the enlistment
     * (including all sub-directories recursively). Not necessarily the
     * same as 'root' for cases where the codebase to process
     * starts in a subdirectory of the enlistment.
     */
    rootTraversal?: Directory;

    /**
     * Build-wide output directories to be added in addition to the ones BuildXL predicts.
     */
    additionalOutputDirectories?: Directory[];

    /**
     * Individual files to flag as untracked
     */
    untrackedFiles?: File[];

    /**
     * Individual directories to flag as untracked
     */
    untrackedDirectories?: Directory[];

    /**
     * Cones (directories and its recursive content) to flag as untracked
     */
    untrackedDirectoryScopes?: Directory[];

    /**
     * Whether pips scheduled by this resolver should run in an isolated container
     * For now running in a container means that outputs will always be created in unique locations
     * and merged back. No merge policies are available at this point, but they will likely be available.
     * Defaults to false.
     * In the future, this might also mean input isolation.
     */
    runInContainer?: boolean;

    /**
     * Collection of directories to search for the required MsBuild assemblies and MsBuild.exe (aka toolset).
     * If not specified, locations in %PATH% are used.
     * Locations are traversed in specification order.
    */
    msBuildSearchLocations?: Directory[];

    /**
     * Targets to execute on the entry point project.
     * If not provided, the default targets are used.
     */
    initialTargets?: string[];

    /**
     * Optional file name for the project or solution that should be used to start parsing (under the root traversal)
     * If not provided, Domino will try to find a candidate under the root traversal
     */
    fileNameEntryPoint?: PathAtom;

    /**
     * Environment that is exposed to MSBuild. If not defined, the current process environment is exposed
     * Note: if this field is not specified any change in an environment variable will potentially cause
     * cache misses for all pips. This is because there is no way to know which variables were actually used during the build.
     * Therefore, it is recommended to specify the environment explicitly.
     */
    environment?: Map<string, (PassthroughEnvironmentVariable | string)>;

    /**
     * Global properties to use for all projects.
     */
    globalProperties?: Map<string, string>;

    /**
     * Activates MSBuild file logging for each MSBuild project file to 'msbuild.log' in the log directory,
     * using the specified MSBuild log verbosity.
     * WARNING: This option adds I/O overhead to your build, since MSBuild console logging is already enabled
     * and captured, and use of Detailed or Diagnostic levels should only be used temporarily to avoid
     * significantly increased build times.
     * If not specified, defaults to "normal"
     */
    logVerbosity?: "quiet" | "minimal" | "normal" | "detailed" | "diagnostic";

    /**
     * Controls whether MSBuild binlog tracing should be enabled for the build.
     * The binlog is placed in the logs directory for each MSBuild project as 'msbuild.binlog'.
     * WARNING: This option increases build I/O and should only be used temporarily to avoid
     * increased build times. Defaults to false.
     */
    enableBinLogTracing?: boolean;

    /**
     * Controls whether MSBuild engine/scheduler tracing should be enabled for the build.
     * WARNING: Use this option only temporarily as it will significantly increase build times.
     * Defaults to false.
     */
    enableEngineTracing?: boolean;
}

interface ToolConfiguration {
    toolUrl?: string;
    hash?: string;
}

interface NuGetConfiguration extends ToolConfiguration {
    credentialProviders?: ToolConfiguration[];
}

type Resolver = DScriptResolver | NuGetResolver | MsBuildResolver;
