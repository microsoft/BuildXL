// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <reference path="Prelude.Core.dsc"/>
/// <reference path="Prelude.IO.dsc"/>
/// <reference path="Prelude.Configuration.dsc"/>
/// <reference path="Prelude.Transformer.Arguments.dsc"/>

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

    /** List of modules with respecting path where to look for this module or its inlined version. */
    modules?: (File | InlineModuleDefinition)[];

    /** Whether specs under this resolver's root should be evaluated as part of the build. */
    definesBuildExtent?: boolean;
}

/**
 * An inline definition of a DScript module which doesn't require a module file to be created
 */
interface InlineModuleDefinition {

    /** The module name.
     * If not provided an internal identified will be assigned. This means the module name will not be known upfront, and
     * therefore other modules won't be able to reference it
     */
    moduleName?: string;
    
    /**
     * The collection of projects that are owned by the module.
     * If not provided, all the .dsc files in the same folder as the main configuration file will be included
     */
    projects?: (Path | File)[];

    /**
     * List of directories to scrub if this module is present in the pip graph.
     */
    scrubDirectories?: Path[];
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
    packages?: {
        id: string;
        version: string;
        alias?: string;
        tfm?: string;
        osSkip?: OsType[];
        dependentPackageIdsToSkip?: string[],
        dependentPackageIdsToIgnore?: string[],
        forceFullFrameworkQualifiersOnly?: boolean,
        filesToExclude?: RelativePath[],
    }[];

    /**
     * Whether to enforce that the version range specified for dependencies in a NuGet package
     * match the package version specified in the configuration file.
     * This is enforced if not specified */
    doNotEnforceDependencyVersions?: boolean;

    /**
     * Specify Esrp sign configuration to sign the dll and exe binary files in nuget packages with Microsoft Certificate
     */
    esrpSignConfiguration?: EsrpSignConfiguration;

    /**
     * When true, includes in the analysis nuget package dependencies whose target frameworks are expressed using a
     * moniker (e.g. 'net6.0') in addition to the usual framework name (e.g. '.NETCoreAppv6.0').
     * Temporary flag to be able to deploy this change. Monikers should be always considered afterwards.
     */
    includeMonikersInNuspecDependencies?: boolean;
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

    /**
     * The name of the value that points to the downloaded content for other resolvers to consume.
     * Defaults to 'download' if not specified.
     * This value will be exposed with type 'File'
     */
    downloadedValueName?: string,

    /**
     * The name of the value that points to the extracted content of the downloaded content for other resolvers to consume.
     * Defaults to 'extracted' if not specified.
     * This value will be exposed with type 'StaticDirectory'
     */
    extractedValueName?: string,
}

/** We represent a passthrough environment variable with the value unit */ 
type PassthroughEnvironmentVariable = Unit;

/** An environment variable can be defined to have a value of the following types */
type EnvironmentData = string | number | Path | PathFragment | CompoundEnvironmentData | Directory;

/** A compound environment variable value. The separator defaults to the OS default if not defined */
interface CompoundEnvironmentData {
    separator?: string; 
    contents: EnvironmentData[];
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
    additionalOutputDirectories?: (Path | RelativePath)[];

    /**
     * Whether pips scheduled by this resolver should run in an isolated container
     * For now running in a container means that outputs will always be created in unique locations
     * and merged back. No merge policies are available at this point, but they will likely be available.
     * Defaults to false.
     * In the future, this might also mean input isolation.
     */
    runInContainer?: boolean;

    /**
     * Collection of directories to search for the required MsBuild assemblies and MsBuild.exe/MSBuild.dll (a.k.a. MSBuild toolset).
     * If specified, locations are traversed in specification order.
     * If not specified, the location of MsBuild will be determined using the default of Microsoft build locator (https://github.com/Microsoft/MSBuildLocator).
     */
    msBuildSearchLocations?: Directory[];

    /**
     * Whether to use the full framework or dotnet core version of MSBuild. Selected runtime is used both for build evaluation and execution.
     * Default is full framework.
     * Observe that using the full framework version means that msbuild.exe is expected to be found in msbuildSearchLocations.
     * If using the dotnet core version, the same logic applies but to msbuild.dll
     */
    msBuildRuntime?: "FullFramework" | "DotNetCore";

    /**
     * Collection of directories to search for dotnet.exe, when DotNetCore is specified as the msBuildRuntime. If not 
     * specified, locations in %PATH% are used.
     * Locations are traversed in specification order.
     * It has no effect if the specified MSBuild runtime is full framework.
     */
    dotNetSearchLocations?: Directory[];
    
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
     * The value can be either EnvironmentData or a PassthroughEnvironmentVariable, the latter representing that the associated variable will be exposed
     * but its value won't be considered part of the build inputs for tracking purposes. This means that any change in the value of the 
     * variable won't cause a rebuild.
     */
    environment?: Map<string, (PassthroughEnvironmentVariable | EnvironmentData)>;

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

    /**
     * Whether VBCSCompiler is allowed to be launched as a service to serve managed compilation requests.
     * Defaults to on.
     * This option will only be honored when process breakaway is supported by the underlying sandbox. Otherwise,
     * it defaults to false.
     */
    useManagedSharedCompilation?: boolean;
}

/**
 * A Rush resolver can either use rush-lib or @rushstack/rush-build-graph-plugin to construct a build graph.
 */
type RushResolver = RushLibResolver | RushBuildGraphPluginResolver;

/**
 * Resolver for Rush project-level build execution. This flavor of the Rush resolver uses rush-lib
 * to retrieve the graph, which returns project-level information. Script-to-script dependencies
 * can be specified as a refinement at the BuildXL config level. See JavaScriptResolverWithExecutionSemantics.
 */
interface RushLibResolver extends RushResolverBase, JavaScriptResolverWithExecutionSemantics {
    graphConstructionMode?: "rush-lib";

    /**
     * The base directory location to look for @microsoft/rush-lib module, used to build the project graph
     * If not provided, BuildXL will try to look for a rush installation under PATH.
     */
    rushLibBaseLocation?: Directory;
}

/**
 * Resolver for Rush script-level build execution. This flavor of the Rush resolver uses @rushstack/rush-build-graph-plugin
 * to retrieve a graph that returns script-level information. See JavaScriptResolverWithoutExecutionSemantics.
 */
interface RushBuildGraphPluginResolver extends RushResolverBase, JavaScriptResolverWithoutExecutionSemantics {

    graphConstructionMode?: "rush-build-graph";

    /**
     * The location of Rush used to build the project graph. The provided instance is assumed to have 
     * @rushstack/rush-build-graph-plugin installed, so --drop-graph is an available CLI that can be used to pass to Rush.
     * If not provided, BuildXL will try to look for a rush installation under PATH.
     */
    rushLocation?: File;

    /**
     * The command passed to the plugin. This includes custom commands that may be defined on a per-repo basis. 
     * See https://rushjs.io/pages/maintainer/custom_commands. 
     * Defaults to 'build'
     */
    rushCommand?: string;

    /**
     * Additional custom parameters to be passed to the plugin. Check https://rushjs.io/pages/maintainer/custom_commands/.
     * Additional parameters can be just flags, e.g. '--production', or name value pairs, e.g. {name: '--locale', value: 'en-us'}
     * Parameter values need to start with a '-' and contain no spaces.
     */
    additionalRushParameters?: (string | {name: string, value: string})[]
}

/**
 * Base interface for a rush-based resolver, which defines
 * the resolver kind and common configuration across the different
 * flavors of a Rush resolver.
 */
interface RushResolverBase extends JavaScriptResolver {
    kind: "Rush";

    /**
     * The graph construction mode to use. See RushBuildGraphPluginResolver and RushLibResolver.
     * If not specified (and it cannot be inferred from other fields), rush-lib is used.
     */
    graphConstructionMode?: "rush-build-graph" | "rush-lib";

    /**
     * Uses each project shrinkwrap-deps.json as a way to track changes in dependencies instead of tracking all actual file dependencies 
     * under the Rush common temp folder.
     * Setting this option improves the chances of cache hits when compatible dependencies are placed on disk, which may not be the same ones
     * used by previous builds. It may also give some performance advantages since there are actually less files to hash and track for changes.
     * However, it opens the door to underbuilds in the case any package.json is modified and BuildXL is executed without 
     * running 'rush update/install' first, since shrinkwrap-deps.json files may be out of date.
     * Defaults to false.
     */
    trackDependenciesWithShrinkwrapDepsFile?: boolean;
}

/**
 * Resolver for Yarn project-level build execution
 */
interface YarnResolver extends JavaScriptResolverWithExecutionSemantics {
    kind: "Yarn";

    /**
     * The location of yarn. If not provided, BuildXL will try to look for it under PATH.
     * A file pointing to yarn can be provided, or alternatively a collection of directories to look for yarn.
     */
    yarnLocation?: File | Directory[];
}

/**
 * Resolver for customized JavaScript repos which are not under any known JavaScript package manager/coordinator.
 */
interface CustomJavaScriptResolver extends JavaScriptResolverWithExecutionSemantics {
    kind: "CustomJavaScript",
    
    /** 
     * A custom project graph that defines the packages and its dependencies.
     * The user can return a file that is expected to be a JSON following the Yarn workspaces
     * schema (https://classic.yarnpkg.com/en/docs/cli/workspaces/#toc-yarn-workspaces-info) 
     * or provide an equivalent map from project names to location and dependencies.
     * If corresponding package.json files are not in place, this customization plays nicely with
     * 'customScripts' (a customization option common to all JavaScript resolvers)
     * */
    customProjectGraph: File | Map<string, {location: RelativePath, workspaceDependencies: string[]}>
}

/**
 * Resolver for Lage project-level build execution
 */
interface LageResolver extends JavaScriptResolverWithoutExecutionSemantics {
    kind: "Lage";

    /**
     * The location of NPM.  If not provided, BuildXL will try to look for it under PATH.
     *  Npm is used to get Lage during graph construction.
     * This and lageLocation cannot be explicitly provided simultaneously.
     */
    npmLocation?: File;

    /**
     * The location of Lage.
     * Lage is used to get the build graph.
     * This and npmLocation cannot be explicitly provided simultaneously.
     */
    lageLocation?: File;

    /**
     * Instructs Lage to generate a subset of the build graph that contains only the nodes that have changed since the given commit. See href="https://microsoft.github.io/lage/docs/Reference/cli".
     * Warning: scoping down builds with '--since' may introduce unsound incremental behavior. Lage filters out the build graph based on git changes without considering the build graph may be underspecified. 
     * For example, if a project does not declare a dependency on another project, then the build graph will not be scoped down correctly. This could even be the case in a DFA-free build, where, for example,
     * a project does not declare a dependency on an out-of-project *source* file (since BuildXL allows JavaScript pips to consume any source file as long as there are no races).
     */
    since?: string;
}

/**
 * Resolver for Nx project-level build execution
 */
interface NxResolver extends JavaScriptResolverWithoutExecutionSemantics {
    kind: "Nx";

    /**
     * The location of Nx libraries. E.g. /usr/lib/nx/node_modules/nx
     * Nx is used to get the build graph and as an executor for each node.
     */
    nxLibLocation?: Directory;
}

/**
 * Base resolver for all JavaScript-like resolvers. E.g. Rush
 */
interface JavaScriptResolver extends ResolverBase, UntrackingSettings {
    /**
     * The repo root
     */
    root: Directory;

    /**
     * The name of the module exposed to other DScript projects that will include all projects found under
     * the enlistment
     */
    moduleName: string;

    /**
     * Environment that is exposed to JavaScript. If not defined, the current process environment is exposed
     * Note: if this field is not specified any change in an environment variable will potentially cause
     * cache misses for all pips. This is because there is no way to know which variables were actually used during the build.
     * Therefore, it is recommended to specify the environment explicitly.
     * The value can be either EnvironmentData or a PassthroughEnvironmentVariable, the latter representing that the associated variable will be exposed
     * but its value won't be considered part of the build inputs for tracking purposes. This means that any change in the value of the 
     * variable won't cause a rebuild.
     */
    environment?: Map<string, (PassthroughEnvironmentVariable | EnvironmentData)> 
                | { [name:string]: (PassthroughEnvironmentVariable | EnvironmentData) };

    /**
     * For debugging purposes. If this field is true, the JSON representation of the project graph file is not deleted.
     */
    keepProjectGraphFile?: boolean;

    /**
     * The path to node.exe to use for discovering the graph.
     * A file pointing to node can be provided, or alternatively a collection of directories to look for node.
     * If not provided, node.exe will be looked in PATH.
     */
    nodeExeLocation?: File | Directory[];

    /**
     * Collection of additional output directories pips may write to.
     * If a relative path is provided, it will be interpreted relative to every project root.
     */
    additionalOutputDirectories?: (Path | RelativePath)[];

    /**
     * Extra dependencies that can be specified for selected projects.
     * Dependencies can be declared against JavaScript projects or regular
     * files or directories.
     * These additional dependencies are added to the build graph after
     * the regular project-to-project ones are computed
     */
    additionalDependencies?: JavaScriptDependency[];

    /**
     * Defines a collection of custom JavaScript commands that can later be used as part of 'execute'.
     */
    customCommands?: JavaScriptCustomCommand[];

    /**
     * Instructs the resolver to expose a collection of exported symbols that other resolvers can consume.
     * Each exported value will have type SharedOpaqueDirectory[], containing the output directories of the specified projects.
     */
    exports?: JavaScriptExport[];

    /**
     * When set, the execution of a script command is considered to have failed if the command writes to standard error, regardless
     * of the script command exit code.
     * Defaults to false.
     */
    writingToStandardErrorFailsExecution?: boolean;

    /**
     * When set, writes under each project node_modules folder is blocked.
     * Defaults to false.
     */
    blockWritesUnderNodeModules?: boolean;

    /**
     * Policy to apply when a double write occurs. By default double writes are only allowed if the produced content is the same.
     */
    doubleWritePolicy?: DoubleWritePolicy;

    /**
     * When specified, the resolver will give this callback an opportunity to schedule pips based on each project information. The callback
     * will be executed for every project discovered by this resolver. When the callback is present, the resolver won't schedule the given 
     * project and the callback is responsible for doing it.
     * The callback defines the location a function whose expected type is (JavaScriptProject, any) => TransformerExecuteResult | TransformerIpcSendResult.
     * The resolver will create an instance of an JavaScriptProject for each discovered project and pass it along.
     * The callback can decide not to schedule a given project by returning 'undefined', in which case the resolver will schedule it in the
     * regular way.
     */
    customScheduling?: CustomSchedulingCallBack;

    /**
     * Users can specify a callback that defines the available scripts for a given package name. The result of the callback
     * overrides the information on existing package.json files that coordinators may decide to pick up.
     * The callback can return a JSON file which is expected to follow package.json schema (in particular, BuildXL will load
     * the 'scripts' section) or an equivalent map of script command name to script content.
     * The callback can return 'undefined' indicating no particular customization should happen for a given package and that
     * the regular way of determining a package script commands should occur.
     */
    customScripts?: (packageName: string, location: RelativePath) => File | Map<string, FileContent>

    /** 
     * A custom set of success exit codes that applies to all processes scheduled by this resolver. 
     * Any other exit code would indicate failure. If unspecified, by default, 0 is the only successful exit code. 
     * */
    successExitCodes?: number[];

    /** 
     * A custom set of exit codes that causes pips to be retried by BuildXL. 
     * Applies to all processes scheduled by this resolver. 
     * If an exit code is also in the successExitCode, then the pip is not retried on exiting with that exit code. 
     * */
    retryExitCodes?: number[];

    /**
     * Maximum number of retries for processes.
     * Applies to all processes scheduled by this resolver. 
     * A process returning an exit code specified in 'retryExitCodes' will be retried at most the specified number of times.
     */
    processRetries?: number;

    /**
     * A custom set of exit codes that will prevent pips from being cached by BuildXL.
     * Applies to all the processes scheduled by this resolver.
     * */
    uncacheableExitCodes?: number[];

    /**
     * When enabled, pips are only allowed to read sources under package roots to which there is an explicitly dependency declared (or is in its transitive closure)
     * Defaults to false.
     * When a pip reads a source file outside of the allowed scopes, a read DFA will be issued.
     * Additional read scopes can be configured with additionalSourceReadsScopes
     */
    enforceSourceReadsUnderPackageRoots?: boolean;

    /**
     * When enforceSourceReadsUnderPackageRoots is enabled, a collection of additional scopes
     * are added to all pips, where source reads are allowed.
     * An additional scope can be provided as a:
     * - Directory: read accesses recursively under the directory will be allowed
     * - string: read accesses matching the regular expression will be allowed
     * When enforceSourceReadsUnderPackageRoots is disabled, this option has no effect.
     */
    additionalSourceReadsScopes?: (Directory | string | JavascriptScopeWithSelector)[];

    /**
     * Pip time out for selected projects.
     * timeouts:[
     *  {
     *      timeout: 5s,
     *      warningTimeout: 5s,
     *      projectSelector: ["@ms/projectA", "@ms/projectB",...,],
     *  },
     *  ...
     * ]
     */
    timeouts?: JavaScriptProjectTimeout[];

    /** 
     * Regex that would be used to extract warnings from the output. 
     */
    warningRegex?: string;

    /** 
     * Regex that would be used to extract errors from the output. 
     */
    errorRegex?: string;

    /**
     * The name of the environment variable BuildXL will use to communicate the number of times pips have been retried so far.
     * When defined, the first time a pip is executed the value of this environment variable will be 0. If a retry happens by virtue of 'retryExitCodes',
     * the variable will have value 1, and so on for subsequent retries.
     * This variable will automatically become a passthrough one and will have no effects on caching.
     */
    retryAttemptEnvironmentVariable?: string;
}

/**
 * Specifies the location of a callback used for scheduling a project in a custom way
 */
interface CustomSchedulingCallBack {
    /** Module name where the callback is defined */
    module: string;
    
    /** 
     * Function name of the callback. The name can be a dotted identifier specifying a function name nested in namespaces. 
     * The type of the defined function is expected to be (JavaScriptProject, any) => TransformerExecuteResult | TransformerIpcSendResult
     * */
    schedulingFunction: string;

    /**
     * An optional argument for the scheduling function. Can be any expression as long as its 
     * type matches the signature of the provided scheduling function.
     * Note: the value of the argument will be evaluated only once, and that value will be used 
     * in all invocations of the scheduling function.
     */
    argument?: LazyEval<any>;
}

/**
 * A JavaScriptDependency configures all dependents to depend on all specified dependencies.
 * The way to specify additional files are directories are via LazyEval expressions to enable
 * importing values from other modules at configuration time.
 */
interface JavaScriptDependency {
    dependencies: (JavaScriptProjectSelector | LazyEval<File> | LazyEval<StaticDirectory>)[], 
    dependents: JavaScriptProjectSelector[]
}


/**
 * Timeout and Warning Timeout for selected projects.
 */
interface JavaScriptProjectTimeout {
    /**
     * How long to wait before terminating individual processes. 
     * The argument allows an expression that represents a time duration, like "3s", "500ms", "30m", "1.5h". 
     * The allowed suffixes are 'ms', 's', 'm', 'h', and no suffix is interpreted as an amount in milliseconds.
     * Setting this value will only have an effect the selected JavaScript project
     */
    timeout?: string;

    /**
     * After how much time to issue a warning that an individual process runs too long. 
     * The argument allows an expression that represents a time duration, like "3s", "500ms", "30m", "1.5h". The allowed suffixes are 'ms', 's', 'm', 'h', and no suffix is interpreted as an amount in milliseconds. 
     * Setting this value will only have an effect the selected JavaScript project.
     */
    warningTimeout?: string;

    /**
     * ProjectSelector that select the projects to apply the timeout
     */
    projectSelector: JavaScriptProjectSelector[];
}


/**
 * The list of commands to execute can be specified with finer-grained detail
 */
interface JavaScriptResolverWithExecutionSemantics extends JavaScriptResolver {
    /**
     * The list of command script names to execute on each project. 
     * Dependencies across commands can be specified. If a simple string is provided in the list, the command with that name will depend 
     * on the command that precedes it on the list, or if it is the first one, on the same command of all its project dependencies.
     * For example: if project A defines commands: ["build", "test"] and project A declares B and C as project dependencies, then 
     * the build command of A will depend on the build command of both B and C. The test command of A will depend on the build command of A.
     * Additionally, finer grained dependencies can be specified using a JavaScriptCommand. In this case, a list of dependencies for each command
     * can be explicitly provided, indicating whether the dependency is on a command on the same project (local) or on a command on all the project 
     * dependencies (project). The specified order in the list is irrelevant for JavaScriptCommands.
     * If not provided, ["build"] is used.
     * Any command specified here that doesn't have a corresponding script is ignored.
     */
    execute?: (string | JavaScriptCommand | JavaScriptCommandGroupWithDependencies)[];   
}

/**
 * The list of commands to execute already represent script-level execution (as opposed to project-level). There is no
 * additional dependency semantics described at BuildXL level.
 */
interface JavaScriptResolverWithoutExecutionSemantics extends JavaScriptResolver {
    /**
     * The script command names to execute.
     * Individual names can be provided, e.g. ["build", "test"] to indicate what script commands to include in the build. The
     * dependencies across script commands will honor what the build graph specifies.
     * Script commands can also be grouped, e.g. ["prepare", {commandName:"build-and-postbuild", commands:["build", "postbuild"], "test"}]. This
     * instructs BuildXL to treat the command group as a single unit of scheduling, which sequentially executes each command. Any other build
     * script depending on the individual commands of a group will be depending on the group itself
     */
    execute?: (string | JavaScriptCommandGroup)[];
}


/**
 * An exported value to other resolvers. 
 * A symbol name must be specified (for now, no namespaces are allowed, just a plain name, e.g. 'outputs').
 * The resolver will expose a 'symbolName' declaration whose value at runtime will be an array of StaticDirectory, with all the output directories 
 * from the projects specified as content.
 */
interface JavaScriptExport {
    symbolName: string;
    content: JavaScriptProjectOutputSelector[];
}

/**
 * A JavaScript project as it was discovered by the resolver. An instance of this interface is passed to the custom scheduling
 * callback.
 * Some information comes directly from the data provided by the corresponding JS coordinator, such as name, script command etc. Some other
 * information (such as inputs, outputs, environment variables, etc.) is computed by the resolver as if the project is going to be
 * scheduled in its usual way, but instead provided to the custom scheduler to be able to make decisions based on it.
 */
interface JavaScriptProject {
    name: string;
    scriptCommandName: string;
    scriptCommand: string;
    projectFolder: Directory;
    inputs: (File | StaticDirectory)[];
    outputs: (Path | Directory)[];
    environmentVariables: {name: string, value: string}[];
    passThroughEnvironmentVariables: string[];
    tempDirectory?: Directory;
    timeoutInMilliseconds?: number;
    warningTimeoutInMilliseconds?: number;
}

/**
 * The following are kept for back-compat reasons. Please use JavaScriptProjectSelector instead
 */
type JavaScriptProjectOutputSelector = JavaScriptProjectSelector;
type JavaScriptProjectOutputs = JavaScriptProjectSelector;


/**
 * A project selector can be 
 * 1) a package name that will be matched against names declared in package.json) and all commands for that package will be included
 * 2) A JavaScriptProjectSimpleSelector, where specific script commands can be specified for a given package or
 * 3) A JavaScriptProjectRegexSelector, where a regular expression is used to match package names and script commands
 */
type JavaScriptProjectSelector = string | JavaScriptProjectSimpleSelector | JavaScriptProjectRegexSelector;

/**
 * A project with a name as specified in its corresponding package.json, together with a collection of script commmands
 */
interface JavaScriptProjectSimpleSelector {
    packageName: string;
    commands: string[];
}

/**
 * Matches a set of package names (as specified in package.json) and script commands
 * by applying the given regular expressions.
 * If commandRegex is not provided, it is interpreted as '.*'.
 */
interface JavaScriptProjectRegexSelector {
    packageNameRegex: string;
    commandRegex?: string;
}

/** 
 * Defines a scope to apply only to projects picked out by some JavaScriptProjectSelector
 */
interface JavascriptScopeWithSelector
{
    scope: Directory | string,
    packages: JavaScriptProjectSelector[]
}

/**
 * Likely to be extended with other types of commands (e.g. a way to add commands as if they were specified in package.json)
 */
type JavaScriptCustomCommand = ExtraArgumentsJavaScript;

/**
 * Appends extra arguments to the corresponding script defined in package.json for every JavaScript project. 
 * If a given project does not define the specified script it has not effect on it.
  */
interface ExtraArgumentsJavaScript {
    command: string;
    extraArguments: JavaScriptArgument | JavaScriptArgument[];
}

type JavaScriptArgument = string | PathAtom | RelativePath | Path;

/**
 * A JavaScript command where depedencies on other commands can be explicitly provided
 * E.g. {command: "test", dependsOn: {kind: "local", command: "build"}} makes the 'test' script depend on the 'build' script
 * of the same project. 
 * Dependencies on other commands of direct dependencies can be specified as well. For example:
 * {command: "localize", dependsOn: {kind: "project", command: "build"}} makes the 'localize' script depend on the 'build' script
 * of all of the project declared dependencies
 */
interface JavaScriptCommand {
    command: string;
    dependsOn: JavaScriptCommandDependency[];
}

/**
 * A sequence of commands that will be executed as a single unit. The execution order will honor the sequence specified of commands
 * specified. A command in the sequence will be executed if the previous one succeeded.
 * The commandName can be used for specifying dependencies as if it was a regular command.
 */
interface JavaScriptCommandGroup {
    commandName: string;
    commands: string[];
}

/**
 * The version of a command group for resolvers extending JavaScriptResolverWithExecutionSemantics
 */
interface JavaScriptCommandGroupWithDependencies extends JavaScriptCommandGroup {
    dependsOn: JavaScriptCommandDependency[];
}

/**
 * A JavaScript command can have 'local' dependencies, meaning dependencies on commands of the same project (e.g. test depends on build)
 * or 'package' to specify a dependency on a command from all its direct dependencies.
 */
interface JavaScriptCommandDependency {
    kind: "local" | "package"; 
    command: string
}

/**
 * Resolver for projects specified for the Ninja build system
 */
interface NinjaResolver extends ResolverBase, UntrackingSettings {
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
    root?: Directory;

    /* The build file, typically build.ninja. If null, f`${root}/build.ninja` is used */
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
    keepProjectGraphFile?: boolean;

    /**
     * Environment that is exposed to Ninja pips. If not defined, the current process environment is exposed
     * The value can be either EnvironmentData or a PassthroughEnvironmentVariable, the latter representing that the associated variable will be exposed
     * but its value won't be considered part of the build inputs for tracking purposes. This means that any change in the value of the 
     * variable won't cause a rebuild.
     */
    environment?: Map<string, (PassthroughEnvironmentVariable | EnvironmentData)>;

    /**
     * Collection of additional output directories pips may write to.
     * If a relative path is provided, it will be interpreted relative to the build's root.
     */
    additionalOutputDirectories?: (Path | RelativePath)[];
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
     * Individual directories to flag as untracked.
     * A relative path is interpreted as relative to the corresponding project root
     */
    untrackedDirectories?: (Directory | RelativePath)[];

    /**
     * Cones (directories and its recursive content) to flag as untracked.
     * A relative path is interpreted as relative to the corresponding project root
     */
    untrackedDirectoryScopes?: (Directory | RelativePath)[];

    /**
     * Cones (directories and its recursive content) to flag as untracked for all projects in the build.
     * The relative path is interepreted relative to each available project
     */
    untrackedGlobalDirectoryScopes?: RelativePath[];

    /**
     * Processes that will break away from the sandbox when spawned by the main process. 
     * The breakaway condition can be specified as process names (with a PathAtom) or by a richer 
     * structure (with a BreakawayProcess), which allows to include conditions on the 
     process arguments.
     * The accesses of processes that break away from the sandbox won't be observed.
     * Processes that breakaway can survive the lifespan of the sandbox.
     * Only add to this list processes that are trusted and whose accesses can be safely predicted
     * by some other means.
     */
    childProcessesToBreakawayFromSandbox?: (PathAtom | BreakawayProcess)[];

    /** 
     * The process names, e.g. "mspdbsrv.exe", allowed to be cleaned up by a process pip sandbox job object
     * after the main process has exited (which would otherwise throw a build error DX0041). 
     * Observe this doesn't mean the process is allowed to survive the sandbox, only that if it tries to survive
     * bxl will terminate it without flagging the corresponding pip as failed.
     */
    allowedSurvivingChildProcesses?: PathAtom[];

    /** 
     * The timeout in milliseconds that the execution sandbox waits for child processes
     * started by the top-level process to exit after the top-level process exits.
     * Defaults to 30000 (30 seconds). 
     */
    nestedProcessTerminationTimeoutMs?: number;
}

/** The characteristics to match for a process to break away from the sandbox */
interface BreakawayProcess {
    /** The breakaway child process name */
    processName: PathAtom,
    
    /** Optionally, the substring that the command line arguments to processName must contain for it to breakaway. */
    requiredArguments?: string,

    /** Whether to ignore case when checking if the command line arguments contain the required arguments */
    requiredArgumentsIgnoreCase?: boolean
}

interface NuGetConfiguration extends ToolConfiguration {
    /**
     * The download timeout, in minutes, for each NuGet download pip. Defaults to 20m.
     * Equivalent to configuring timeoutInMilliseconds for the corresponding Transformers.ToolDefinition
     */
    downloadTimeoutMin?: number
}

interface EsrpSignConfiguration {

    /** Sign tool exe file */
    signToolPath: Path;

    /** ESRP session information config, ESRPClient's -c argument */
    signToolConfiguration: Path;

    /** ESRP policy information config, ESRPClient's -p argument */
    signToolEsrpPolicy: Path;

    /** EsrpAuthentication.json */
    signToolAadAuth: Path;
}

interface ScriptResolverDefaults {

}

interface NuGetResolverDefaults {

}

interface MsBuildResolverDefaults {

}

type Resolver = DScriptResolver | NuGetResolver | DownloadResolver | MsBuildResolver | NinjaResolver | RushResolver | YarnResolver | LageResolver | CustomJavaScriptResolver | NxResolver;
