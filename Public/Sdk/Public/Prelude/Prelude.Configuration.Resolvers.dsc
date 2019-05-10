// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <reference path="Prelude.Core.dsc"/>
/// <reference path="Prelude.IO.dsc"/>
/// <reference path="Prelude.Configuration.dsc"/>

/**
 * Source resolver that uses specified source paths for module resolution.
 */
interface DScriptResolver extends ResolverBase {
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
interface NuGetResolver extends ResolverBase {
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
    packages?: {id: string; version: string; alias?: string; tfm?: string; dependentPackageIdsToSkip?: string[], dependentPackageIdsToIgnore?: string[], forceFullFrameworkQualifiersOnly?: boolean}[];

    /**
     * Whether to enforce that the version range specified for dependencies in a NuGet package
     * match the package version specified in the configuration file.
     * This is enforced if not specified */
    doNotEnforceDependencyVersions?: boolean;
}

interface DownloadResolver extends ResolverBase {
    kind: "Download",
    downloads: DownloadSettings[]
}

/**
 * Setings for a download
 */
interface DownloadSettings {
    /**
     * The name of the module to expose
     */
    moduleName: string,

    /**
     * Url of the download
     */
    url: string,

    /**
     * Optional filename. By default the filename for the download is determined from the URL, but can be overridden when the url is obscure.
     */
    fileName?: string,

    /**
     * Optional declaration of the archive type to indicate how the file should be extracted.
     */
    archiveType?: "file" | "zip" | "gzip" | "tgz" | "tar"

    /**
     * Optional hash of the downloaded file to ensure safe robust builds and correctness. When specified the download is validated against this hash.
     */
    hash?: string,
}

/**
 * Resolver for MSBuild project-level build execution, utilizing the MsBuild static graph API to
 * find MSBuild files and convert them to a pip graph
 */
interface MsBuildResolver extends ResolverBase, UntrackingSettings {
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
     * Whether pips scheduled by this resolver should run in an isolated container
     * For now running in a container means that outputs will always be created in unique locations
     * and merged back. No merge policies are available at this point, but they will likely be available.
     * Defaults to false.
     * In the future, this might also mean input isolation.
     */
    runInContainer?: boolean;

    /**
     * Collection of directories to search for the required MsBuild assemblies and MsBuild.exe (a.k.a. MSBuild toolset).
     * If not specified, locations in %PATH% are used.
     * Locations are traversed in specification order.
    */
    msBuildSearchLocations?: Directory[];

    /**
     * Optional file paths for the projects or solutions that should be used to start parsing. These are relative
     * paths with respect to the root traversal.
     *
     * If not provided, BuildXL will attempt to find a candidate under the root traversal. If more than one candidate
     * is available, the process will fail.
     */
    fileNameEntryPoints?: RelativePath[];

    /**
     * Targets to execute on the entry point project. If not provided, the default targets are used.
     * Initial targets are mapped to /target (or /t) when invoking MSBuild for the entry point project
     * E.g. initialTargets: ["Build", "Test"]
     */
    initialTargets?: string[];

    /**
     * Environment that is exposed to MSBuild. If not defined, the current process environment is exposed
     * Note: if this field is not specified any change in an environment variable will potentially cause
     * cache misses for all pips. This is because there is no way to know which variables were actually used during the build.
     * Therefore, it is recommended to specify the environment explicitly.
     */
    environment?: Map<string, string>;

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

    /**
     * For debugging purposes. If this field is true, the JSON representation of the project graph file is not deleted.
     */
    keepProjectGraphFile?: boolean;

    /**
     * Whether each project has implicit access to the transitive closure of its references.
     * Turning this option on may imply a decrease in build performance but many existing MSBuild repos rely on an equivalent feature.
     * Defaults to false.
     */
    enableTransitiveProjectReferences?: boolean;

    /**
     * When true, MSBuild projects are not treated as first class citizens and MSBuild is instructed to build each project using the legacy mode,
     * which relies on SDK conventions to respect the boundaries of a project and not build dependencies. The legacy mode is less restrictive than the
     * default mode, where explicit project references to represent project dependencies are strictly enforced, but a decrease in build performance and
     * other build failures may occur (e.g. double writes due to overbuilds).
     * Defaults to false.
     */
    useLegacyProjectIsolation?: boolean;

    /**
     * Policy to apply when a double write occurs. By default double writes are only allowed if the produced content is the same.
     */
    doubleWritePolicy?: DoubleWritePolicy;

    /**
     * Whether projects are allowed to not specify their target protocol.
     * When true, default targets will be used as heuristics. Defaults to false.
     */
    allowProjectsToNotSpecifyTargetProtocol?: boolean;
}


/**
 * Resolver for projects specified for the Ninja build system
 */
interface NinjaResolver extends ResolverBase {
    kind: "Ninja";

    /**
     * High-level targets to explore
     * TODO: This probably shouldn't be the user's responsibilty
     */
    targets?: string[];

    /**
     * The root of the project. This should be the directory containing the build.ninja file
     * (or the corresponding .ninja build file if it's named differently).
     * If not present, specFile should be specified and its parent will be the projectRoot
     */
    projectRoot?: Directory;

    /* The build file, typically build.ninja. If null, f`${projectRoot}/build.ninja` is used */
    specFile?: File;

    /**
     * The name of the module exposed to other DScript projects.
     * This should be unique across modules.
     */
    moduleName: string;

    /**
     * Preserve intermediate outputs used to construct the graph,
     * that is, the arguments passed to the tools and the JSON reperesentation
     * of the dependency graph. Useful for debugging.
     * If not present, we don't keep the outputs.
     */
    keepToolFiles?: boolean;

     /**
     * Custom untracking settings
     */
    untrackingSettings?: UntrackingSettings;

    /**
     * Remove all flags involved with the output of debug information (PDB files).
     * If this is true the /Zi, /ZI, /Z7, /FS flags are removed from the command options
     * This option is helpful to troubleshoot debug builds that are failing with related errors
     * Defaults to false.
     */
    RemoveAllDebugFlags?: boolean;
}


/**
 * Resolver for projects specified with CMake
 * This resolver will generate files
 */
interface CMakeResolver extends ResolverBase {
    kind: "CMake";

    /**
     * The root of the project. This should be the directory containing the CMakeLists.txt file
     */
    projectRoot: Directory;

    /**
     * The name of the module exposed to other DScript projects.
     * This should be unique across modules.
     */
    moduleName: string;


    /**
     * The directory where we will build, relative to the BuildXL output folder
     */
    buildDirectory: RelativePath;

    /**
     * When cmake is first run in an empty build tree, it creates a CMakeCache.txt file
     * and populates it with customizable settings for the project.
     * This option may be used to specify a setting that takes priority over the project’s default value.
     * [https://cmake.org/cmake/help/v3.6/manual/cmake.1.html]
     *
     * These values will be passed to the CMake generator as -D<name>=<value> arguments
     * The value can be 'undefined', in which case the variable will be unset (-U<name> will be passed as an argument)
     */
    cacheEntries?: { [name: string]: string; };

    /**
     * Collection of directories to search for cmake.exe.
     * If not specified, locations in %PATH% are used.
     * Locations are traversed in specification order.
    */
    cMakeSearchLocations?: Directory[];

    /**
     * Custom untracking settings
     */
    untrackingSettings?: UntrackingSettings;

    /**
     * Remove all flags involved with the output of debug information (PDB files).
     * If this is true the /Zi, /ZI, /Z7, /FS flags are removed from the command options
     * This option is helpful to troubleshoot debug builds that are failing with related errors
     * Defaults to false.
     */
    RemoveAllDebugFlags?: boolean;
}

interface ToolConfiguration {
    toolUrl?: string;
    hash?: string;
}

interface ResolverBase {
    /**
     * Optional name of the resolver
     * When provided BuildXL will give better error messages and
     * allows grouping in the viewer
     **/
    name?: string;
}

interface UntrackingSettings {
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
}

interface NuGetConfiguration extends ToolConfiguration {
    credentialProviders?: ToolConfiguration[];
}

interface ScriptResolverDefaults {

}

interface NuGetResolverDefaults {

}

interface MsBuildResolverDefaults {

}

type Resolver = DScriptResolver | NuGetResolver | DownloadResolver | MsBuildResolver | NinjaResolver | CMakeResolver;
