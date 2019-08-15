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
interface FileAccessWhitelistEntry {
    /** Name of the whitelist exception rule. */
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
     * Option that enables all V2-related features available at a given time.
    */
    useDominoScriptV2?: boolean;

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
    * The resolution semantics for the package represented by a config.dsc.
    * V2 feature.
    */
    nameResolutionSemantics?: NameResolutionSemantics;

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
    * Office-specific flag. Office passes this so we can ensure that when V2 becomes the default, office maintains existing codepath via this flag if needed.
    */
    useLegacyDominoScript?: boolean;

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
     * Default value to use for the transformer execute argument 'runInContainer', therefore affecting all pips. 
     * Defaults to false.
     * This is an experimental feature for now, use it at your own risk.
     */
    runInContainerDefault?: boolean;
}

const enum TypeCheckerKind {
    /** Type checker that runs DScript language service to validate build specs. */
    typeScriptBasedTypeChecker = 0,

    /** Type checker written in C# */
    managedTypeChecker,
}

interface EngineConfiguration {
    useHardlinks?: boolean;
    scanChangeJournal?: boolean;
    //phase?: EnginePhases; //TODO: add ambient EnginePhases enum?
    maxRelativeOutputDirectoryLength?: number;
    cleanTempDirectories?: boolean;
    defaultFilter?: string;
}

/** The information needed to generate msbuild files */
interface VsDominoConfiguration {
    /** Whether VsDomino is enabled or not. */
    isEnabled?: boolean;
    /** Whether VsDomino generates msbuild project files under the source tree */
    canWriteToSrc?: boolean;
    /** Solution file name that will be generated */
    solutionName?: PathAtom;
    /** VsDomino root directory where the solution and misc files will be written. */
    vsDominoRoot?: File;
    /** Optional resharper dotsettings file to be placed next to the generated solution. */
    dotSettingsFile?: File;
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
     * List of file accesses that are benign and allow the pip that caused them to be cached.
     *
     * This is a separate list from the above, rather than a bool field on the exceptions, because
     * that makes it easier for a central build team to control the contents of the (relatively dangerous)
     * cacheable whitelist.  It can be placed in a separate file in a locked-down area in source control,
     * even while exposing the (safer) do-not-cache-but-also-do-not-error whitelist to users.
     */
    cacheableFileAccessWhitelist?: FileAccessWhitelistEntry[];

    /** List of file access exception rules. */
    fileAccessWhiteList?: FileAccessWhitelistEntry[];

    /** List of rules for the directory membership fingerprinter to use */
    directoryMembershipFingerprinterRules?: DirectoryMembershipFingerprinterRule[];

    /** Overrides for the dependency violations */
    dependencyViolationErrors?: DependencyViolationErrors;

    /** Configuration for front end. */
    frontEnd?: FrontEndConfiguration;

    /** BuildXL engine configuration. */
    engine?: EngineConfiguration;

    searchPathEnumerationTools?: RelativePath[];

    /** List of incremental tools for special observed file access handling */
    incrementalTools?: RelativePath[];
    
    /** Configuration for VsDomino */
    vsDomino?: VsDominoConfiguration;

    /** Whether this build is running in CloudBuild */
    inCloudBuild?: boolean;

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