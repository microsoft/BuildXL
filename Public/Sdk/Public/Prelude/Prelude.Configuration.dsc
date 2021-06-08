// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <reference path="Prelude.Core.dsc"/>
/// <reference path="Prelude.IO.dsc"/>
/// <reference path="Prelude.Configuration.Resolvers.dsc"/>

//=============================================================================
//  Mounts
//=============================================================================

/**
 * Defines a named root
 */
interface Mount {
    name: PathAtom;

    /** Indicates whether reads are allowed under the root. */
    isReadable?: boolean;

    /** (Internal use only) Indicates whether the root represents a system location (such as Program Files). */
    isSystem?: boolean;

    /** Indicates whether writes are allowed under the root. */
    isWritable?: boolean;

    /** Indicates whether a mount may be scrubbed (have files not registered in the current build graph as inputs or outputs deleted). */
    isScrubbable?: boolean;

    /** The absolute path to the root. */
    path: PathQueries;

    /** Indicates whether hashing is enabled under the root. */
    trackSourceFileChanges?: boolean;
}

//=============================================================================
//  Configuration
//=============================================================================
interface FileAccessAllowlistEntry {
    /** Name of the allowlist exception rule. */
    name?: string;

    /** Path or executable name to misbehaving tool allowed to have an exception.  Cannot be combined with Value. */
    toolPath?: Path | File | PathAtom;

    /** Value allowed to have an exception.  Cannot be combined with ToolPath. */
    value?: string;

    /** Pattern to match against accessed paths. */
    pathRegex?: string;

    /** Fragment of a path to match. */
    pathFragment?: string;
}

// Compatibility
interface FileAccessWhitelistEntry {
    /** Name of the allowlist exception rule. */
    name?: string;

    /** Path to misbehaving tool allowed to have an exception.  Cannot be combined with Value. */
    toolPath?: Path | File;

    /** Value allowed to have an exception.  Cannot be combined with ToolPath. */
    value?: string;

    /** Pattern to match against accessed paths. */
    pathRegex?: string;

    /** Fragment of a path to match. */
    pathFragment?: string;
}

interface DirectoryMembershipFingerprinterRule {
    /** Name of the exception */
    name?: string;

    /** Directory with exception */
    root?: Directory;

    /** Disables filesystem based enumeration. Graph based enumeration will be used instead */
    disableFilesystemEnumeration?: boolean;

    /** Wildcards for names of files to exclude from enumeration. Does not apply to files within subdirectories */
    fileIgnoreWildcards?: PathAtom[];
}

interface DependencyViolationErrors {
    /** The override level of the generic violations */
    generic: boolean;

    /** The override level of the double write violations */
    doubleWrite: boolean;

    /** The override level of the read race violations */
    readRace: boolean;

    /** The override level of the undeclared ordered read violations */
    undeclaredOrderedRead: boolean;

    /** The override level of the missing source dependency violations */
    missingSourceDependency: boolean;

    /** The override level of the undeclared read cycle violations */
    undeclaredReadCycle: boolean;

    /** The override level of the undeclared output violations */
    undeclaredOutput: boolean;

    /** The override level of the read undeclared output violations */
    readUndeclaredOutput: boolean;
}

interface FrontEndConfiguration {
    /**If false, then the front-end will process every spec in the build regardless of a previous invocation state. */
    incrementalFrontEnd?: boolean;

    /** If true, then such literals like p``, d``, f`` etc would be converted to internal BuildXL representation at parse time. */
    convertPathLikeLiteralsAtParseTime?: boolean;

    /** Whether to profile DScript evaluations. */
    profileScript?: boolean;

    /** Location of the profiling result. */
    profileReportDestination?: Path;

    /** Location of the json report with all file-to-file relationships for all DScript specs. */
    fileToFileReportDestination?: Path;

    /** Whether to launch DScript debugger on start. */
    debugScript?: boolean;

    /** Whether to break at the end of the evaluation phase. Defaults to off. */
    debuggerBreakOnExit?: boolean;

    /** TCP/IP port for the DScript debugger to listen on. */
    debuggerPort?: number;

    /** Multiplier that will be used to adjust minimum number of threads in the threadpool for evaluation phase. */
    threadPoolMinThreadCountMultiplier?: number;

    /** The max concurrency to use for frontend evaluation. */
    maxFrontEndConcurrency?: number;

    /** The max concurrency for a managed type checker. */
    maxTypeCheckingConcurrency?: number;

    /** The max concurrency to use for restoring nuget packages. */
    maxRestoreNugetConcurrency?: number;

    /**
     * Whether partial evaluation may be used. When not set, the full graph will always be evaluated. When set, a
     * partial graph may be used depending on the filter (default: false for XML).
     * */
    usePartialEvaluation?: boolean;

    /**
     * If true, the front-end will construct the binding fingerprint and will save it along with the spec-2-spec mapping.
     * This information could be used during the subsequent runs to filter workspace without parsing/checking the entire world.
    */
    constructAndSaveBindingFingerprint?: boolean;

    /**
     * Set of "linter" rule names that should be applied during the build.
     * DScript has two set of rules:
     * - predefined rules that restricts TypeScript language (like no `eval`) and
     * - custom set of rules that could be applied only on specific code bases (like no `glob`).
     * This list is a second one and contains a set of configurable rules that could be enabled.
    */
    enabledPolicyRules?: string[];

    /**
     * Whether namespaces are automatically exported if any of its members are exported
     */
    automaticallyExportNamespaces?: boolean;

     /**
     * If true, then new name resolution logic would be based on semantic information.
     */
    useSemanticInformation?: boolean;

     /**
     * If true, then optional language policy analysis would be disabled.
     * For instance, semicolon check would be disabled in this case.
     */
    disableLanguagePolicyAnalysis?: boolean;

     /**
     * If true, additional table will be used in a module literal that allows to resolve entries by a full name.
     * Only applicable in tests, because even with V2 semantic they still use 'by name' value resolution.
     */
    preserveFullNames?: boolean;

     /**
     * Disables cycle detection if set to true.
     */
    disableCycleDetection?: boolean;

     /**
     * Whether the parser preserves trivia. This is not intended to be exposed to the user and it is used for internal tools.
     */
    preserveTrivia?: boolean;

     /**
     * Front end will stop parsing, binding or type checking if the error count exceeds the limit.
     * In some cases a build cone could have so many errors that even printing them on the screen will take too long.
     * This option allows a user to stop the execution when error limit is reached.
     */
    errorLimit?: number;

     /**
     * Forces downloaded packages to be republished to the cache
     */
    forcePopulatePackageCache?: boolean;

     /**
     * If specified all method invocations will be tracked and top N most frequently methods will be captured in the log.
     */
    trackMethodInvocations?: boolean;

     /**
     * Suggested waiting time before starting the cycle detection in seconds.
     */
    cycleDetectorStartupDelay?: number;

     /**
     * If specified, then the BuildXL will fail if workspace-related memory is not collected successfully
     * before evalution phase.
     * This is temporary flag to unblock rolling builds.
     */
    failIfWorkspaceMemoryIsNotCollected?: boolean;

     /**
     * If specified, then BuildXL will fail if frontend-related memory is not collected successfully
     * when the front end has been used
     */
    failIfFrontendMemoryIsNotCollected?: boolean;

     /**
     * Attempt to reload previous graph and use "PatchableGraphBuilder" which allows
     * the front end to patch the graph based on changed spec files.
     */
    useGraphPatching?: boolean;

     /**
     * Attempt to reload previous engine state (path and symbol tables, and graph). This is a precondition for UseGraphPatching
     * but also for other optimizations that need the same tables across builds
     */
    reloadPartialEngineStateWhenPossible?: boolean;

     /**
     * Whether parsing is cancelled when the first spec fails to parse
     * There is not a command-line option for this, and this is not really intended to be user facing (even though
     * if a user decides to set this to false, it will work). This flag is mainly for the IDE to configure the engine.
     */
    cancelParsingOnFirstFailure?: boolean;

     /**
     * Whether the public surface of specs and serialized AST are attempted to be used when available
     */
    useSpecPublicFacadeAndAstWhenAvailable?: boolean;

     /**
     * Whether to cancel any pending evaluation after first error.
     * Currently, there is no separate command-line option for this; instead, the existing /stopOnFirstError switch
     * is used to control both IScheduleConfiguration and this option.
     */
    cancelEvaluationOnFirstFailure?: boolean;

     /**
     * Enables module configuration files to declare a list of cyclical friend modules.
     */
    enableCyclicalFriendModules?: boolean;

     /**
     * Temporary flag that controls whether double underscore identifiers are escaped (e.g. __test).
     * Escaping is the TypeScript behavior, but due to a bug we need to incrementally fix this, and therefore we need this flag.
     */
    escapeIdentifiers?: boolean;

    /**
     * If true the check that a member is obsolete is disabled during Ast Conversion.
     */
     disableIsObsoleteCheckDuringConversion?: boolean;

     /**
      * If true, ignore missing modules files by flagging them with a verbose message rather than an error.
      */
      allowMissingSpecs?: boolean;
}

interface EngineConfiguration {
    useHardlinks?: boolean;
    scanChangeJournal?: boolean;
    //phase?: EnginePhases; //TODO: add ambient EnginePhases enum?
    maxRelativeOutputDirectoryLength?: number;
    cleanTempDirectories?: boolean;
    defaultFilter?: string;
    allowDuplicateTemporaryDirectory? : boolean;
    
    /**
     * Whether to allow writes outside any declared mounts.
     * Defaults to false.
     */
    unsafeAllowOutOfMountWrites?: boolean;
}

interface SandboxConfiguration {
    containerConfiguration?: SandboxContainerConfiguration;
    unsafeSandboxConfiguration?: UnsafeSandboxConfiguration;
}

/**
 * Helium container related configuration. Sets global values for all pips.
 */
interface SandboxContainerConfiguration {
    /**
     * Whether pips should run in a container. Defaults to false.
     * This field can be overriden by individual pips.
     */
    runInContainer?: boolean,

    /**
     * The isolation level for pips running in a container.
     * Defaults to isolate all outputs.
     * This field can be overriden by individual pips.
     */
    containerIsolationLevel?: ContainerIsolationLevel
}

interface UnsafeSandboxConfiguration {
    /**
     * What to do when a double write occurs.
     * By default double writes are blocked. Other options may result in making the corresponding pips unsafe,
     * by introducing non-deterministic behavior.
     */
    doubleWritePolicy?: DoubleWritePolicy;

    /**
     * [Obsolete] This option has no effect and is left for back compat purposes. Please see 
     * 'enableFullReparsePointResolving'
     */
    processSymlinkedAccesses? : boolean;

    /**
     * If enabled, resolves every observed path in the process sandbox before reporting it. The
     * resolving process refers to removing and replacing reparse points with their final targets. By
     * default this is disabled. Only has an effect on Windows-based OS. Mac sandbox already
     * processes reparse points correctly.
     */
    enableFullReparsePointResolving? : boolean;

    /**
     * When true, outputs produced under shared opaques won't be flagged as such.
     * This means subsequent builds won't be able to recognize those as outputs and they won't be deleted before pips run.
     * Defaults to false.
     */
    skipFlaggingSharedOpaqueOutputs?: boolean;
}

type DoubleWritePolicy =
        // double writes are blocked
        "doubleWritesAreErrors" |
        // double writes are allowed as long as the file content is the same
        "allowSameContentDoubleWrites" |
        // double writes are allowed, and the first process writing the output will (non-deterministically)
        // win the race. Consider this will result in a non-deterministic deployment for a given build, and is therefore unsafe.
        "unsafeFirstDoubleWriteWins";

type SourceRewritePolicy = "sourceRewritesAreErrors" | "safeSourceRewritesAreAllowed";

/**
 * The different isolation level options when a process runs in a container
 */
const enum ContainerIsolationLevel {
    // No isolation
    none = 0,
    // Isolate declared output files
    isolateOutputFiles = 0x1,
    // Isolate shared opaque directory content
    isolateSharedOpaqueOutputDirectories = 0x2,
    // Isolate exclusive opaque directory content
    isolatedExclusiveOpaqueOutputDirectories = 0x4,
    // TODO: not implemented
    isolateInputs = 0x8,

    isolateOutputDirectories = isolateSharedOpaqueOutputDirectories | isolatedExclusiveOpaqueOutputDirectories,
    isolateAllOutputs = isolateOutputFiles | isolateOutputDirectories,
    isolateAll = isolateAllOutputs | isolateInputs,
}

/** The information needed to generate MSBuild files. */
interface IdeConfiguration {
    /** Whether Ide Generation is enabled or not. */
    isEnabled?: boolean;
    /** Whether Ide Generation generates MSBuild project files under the source tree */
    canWriteToSrc?: boolean;
    /** Solution file name that will be generated */
    solutionName?: PathAtom;
    /** Ide Generation root directory where the solution and misc files will be written. */
    solutionRoot?: Directory;
    @@obsolete("Please use solutionRoot")
    vsDominoRoot?: File;
    /** Optional resharper dotsettings file to be placed next to the generated solution. */
    dotSettingsFile?: File;
}

/** Default configuration parameters for front-end resolvers. */
interface ResolverDefaults {
    /** Defaults for the DScript front-end resolver. */
    dominoScript?: ScriptResolverDefaults;
    script?: ScriptResolverDefaults;

    /** Defaults for the NuGet front-end resolver. */
    nuget?: NuGetResolverDefaults;
}

interface LoggingConfiguration {
    /**
     * When set to true, the dump pip lite runtime analyzer will be enabled to dump information about failing pips.
     * This option is enabled by default.
     */
    dumpFailedPips?: boolean;

    /**
     * When the dumpFailedPips option is enabled, this flag can be used to limit the number of logs to the specified value.
     * The default value for this option is 50
     */
    dumpFailedPipsLogLimit?: number;
}

interface Configuration {
    qualifiers?: QualifierConfiguration;

    /** Set of projects in the build cone. */
    projects?: File[];

    /** Set of source packages for current build cone.
     * Obsolete. Use 'modules' instead.
    */
    //@@obsolete
    packages?: File[];

    /** Set of source modules for current build cone. */
    modules?: File[];

    /** Set of special objects that are used to resolve actual physical location of the module. */
    resolvers?: Resolver[];

    /** The environment variables that are accessible in the build. */
    allowedEnvironmentVariables?: string[];

    /** The Mounts that are defined in the build */
    mounts?: Mount[];

    /** Disable default source resolver. */
    disableDefaultSourceResolver?: boolean;

    /** 
     * Disable the DScript resolver that loads the SDKs shipped in-box with BuildXL.
     * This resolver owns any in-box SDK shipped with BuildXL under the SDK folder placed where BuildXL binaries are.
     * When enabled, this resolver is implicitly added right after user defined resolvers.
     * Defaults to false.
     */
    disableInBoxSdkSourceResolver?: boolean;

    /**
     * List of file accesses that are benign and allow the pip that caused them to be cached.
     *
     * This is a separate list from the above, rather than a bool field on the exceptions, because
     * that makes it easier for a central build team to control the contents of the (relatively dangerous)
     * cacheable allowlistlist.  It can be placed in a separate file in a locked-down area in source control,
     * even while exposing the (safer) do-not-cache-but-also-do-not-error allowlist to users.
     */
    cacheableFileAccessAllowlist?: FileAccessAllowlistEntry[];
	cacheableFileAccessWhitelist?: FileAccessWhitelistEntry[]; // compatibility

    /** List of file access exception rules. */
	fileAccessAllowList?: FileAccessAllowlistEntry[];
    fileAccessWhiteList?: FileAccessWhitelistEntry[]; // compatibility

    /** List of rules for the directory membership fingerprinter to use */
    directoryMembershipFingerprinterRules?: DirectoryMembershipFingerprinterRule[];

    /** Overrides for the dependency violations */
    dependencyViolationErrors?: DependencyViolationErrors;

    /** Configuration for front end. */
    frontEnd?: FrontEndConfiguration;

    /** BuildXL engine configuration. */
    engine?: EngineConfiguration;

    /** BuildXL sandbox configuration */
    sandbox?: SandboxConfiguration;

    searchPathEnumerationTools?: RelativePath[];

    /** List of incremental tools for special observed file access handling */
    incrementalTools?: RelativePath[];

    /** Configuration for VsDomino */
    ide?: IdeConfiguration;
    vsDomino?: IdeConfiguration;

    /** Whether this build is running in CloudBuild */
    inCloudBuild?: boolean;

    /** Overrides for defaults by front-end resolver. */
    resolverDefaults?: ResolverDefaults;

    /** BuildXL logging configuration */
    logging?: LoggingConfiguration;
}

/** Configuration function that is used in config.ds for configuring a DScript source cone. */
declare function config(configuration: Configuration): void;

/** Legacy configuration function kept for compat reasons. This will eventually be removed. */
declare function configure(configuration: Configuration): void;

//-----------------------------------------------------------------------------
//  Qualifiers
//-----------------------------------------------------------------------------

interface QualifierInstance {
    [name: string]: string;
}

interface QualifierSpace {
    [name: string]: string[];
}

interface QualifierConfiguration {

    /** The default qualifier space for this build */
    qualifierSpace?: QualifierSpace;

    /**
     * The default qualifier to use when none specified on the commandline
     */
    defaultQualifier?: QualifierInstance;

    /** A list of alias for qualifiers that can be used on the commandline */
    namedQualifiers?: {
        [name: string]: QualifierInstance;
    };
}

declare let qualifier : any;

//---------------
// Qualifier V2
//---------------

/**
* Base interface for custom qualifier types.
* Any extensions to this type used to define the type of a current qualifier must *directly* inherit from it.
*/
interface Qualifier {}
