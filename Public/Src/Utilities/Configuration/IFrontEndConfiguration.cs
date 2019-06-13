// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// For compatibility reasons, there is more than one semantics for defining the public surface of a module.
    /// </summary>
    /// <remarks>
    /// This is also an enum exposed in DScript prelude. Please make sure this definition is in sync with Prelude.Packaging.dsc
    /// </remarks>
    [SuppressMessage("Microsoft.Naming", "CA1717:OnlyFlagsEnumsShouldHavePluralNames")]
    public enum NameResolutionSemantics
    {
        /// <summary>
        /// DScript V1 resolution semantics for a module: Explicit project and module imports, with a main file representing the module exports
        /// </summary>
        ExplicitProjectReferences,

        /// <summary>
        /// DScript V2 resolution semantics for a module: Implicit project references but explicit module references
        /// with private/internal/public visibility on values
        /// </summary>
        ImplicitProjectReferences,
    }

    /// <summary>
    /// Mode in which front end operates.
    /// </summary>
    public enum FrontEndMode
    {
        /// <summary>
        /// DebugScript command-line option was provided.
        /// </summary>
        DebugScript,

        /// <summary>
        /// ProfieScript command-line option was provided.
        /// </summary>
        ProfileScript,

        /// <summary>
        /// No special flags were provided.
        /// </summary>
        NormalMode,
    }

    /// <summary>
    /// Front-end configuration interface.
    /// </summary>
    public interface IFrontEndConfiguration
    {
        /// <summary>
        /// If false, then the front-end will process every spec in the build regardless of a previous invocation state.
        /// </summary>
        /// <remarks>
        /// This is a high-level flag to turn off entire logic responsible for constructing and storing the artifact required for incremental parsing/typechecking/conversion
        /// of build specs.
        /// True by default.
        /// </remarks>
        bool? EnableIncrementalFrontEnd { get; }

        /// <summary>
        /// Whether to profile DScript evaluations.
        /// </summary>
        bool? ProfileScript { get; }

        /// <summary>
        /// Location of the profiling result
        /// </summary>
        AbsolutePath? ProfileReportDestination { get; }

        /// <summary>
        /// Location of the json report with all file-to-file relationships for all DScript specs.
        /// </summary>
        AbsolutePath? FileToFileReportDestination { get; set; }

        /// <summary>
        /// Whether to launch DScript debugger on start.
        /// </summary>
        bool? DebugScript { get; }

        /// <summary>
        /// Whether to break at the end of the evaluation phase. Defaults to off.
        /// </summary>
        bool? DebuggerBreakOnExit { get; }

        /// <summary>
        /// TCP/IP port for the DScript debugger to listen on.
        /// </summary>
        int? DebuggerPort { get; }

        /// <summary>
        /// The max concurrency to use for frontend evaluation
        /// </summary>
        int? MaxFrontEndConcurrency { get; }

        /// <summary>
        /// The max concurrency to use for restoring nuget packages.
        /// </summary>
        int? MaxRestoreNugetConcurrency { get; }

        /// <summary>
        /// Multiplier that will be used to adjust minimum number of threads in the threadpool for evaluation phase.
        /// </summary>
        int? ThreadPoolMinThreadCountMultiplier { get; }

        /// <summary>
        /// The max concurrency for a managed type checker.
        /// </summary>
        int? MaxTypeCheckingConcurrency { get; }

        /// <summary>
        /// Whether partial evaluation may be used. When not set, the full graph will always be evaluated. When set, a
        /// partial graph may be used depending on the filter (default: false for XML).
        /// </summary>
        bool? UsePartialEvaluation { get; }

        /// <summary>
        /// If true, the front-end will construct the binding fingerprint and will save it along with the spec-2-spec mapping.
        /// This information could be used during the subsequent runs to filter workspace without parsing/checking the entire world.
        /// </summary>
        bool? ConstructAndSaveBindingFingerprint { get; }

        /// <summary>
        /// Set of "linter" rule names that should be applied during the build.
        /// </summary>
        /// <remarks>
        /// DScript has two set of rules:
        /// - predefined rules that restricts TypeScript language (like no `eval`) and
        /// - custom set of rules that could be applied only on specific code bases (like no `glob`).
        /// This list is a second one and contains a set of configurable rules that could be enabled.
        /// </remarks>
        [NotNull]
        IReadOnlyList<string> EnabledPolicyRules { get; }

        /// <summary>
        /// If true, then optional language policy analysis would be disabled.
        /// </summary>
        /// <remarks>
        /// For instance, semicolon check would be disabled in this case.
        /// </remarks>
        bool? DisableLanguagePolicyAnalysis { get; }

        /// <summary>
        /// The resolution semantics for the package represented by a config.dsc.
        /// </summary>
        /// <remarks>
        /// V2 feature.
        /// </remarks>
        NameResolutionSemantics? NameResolutionSemantics { get; }

        /// <summary>
        /// If true, additional table will be used in a module literal that allows to resolve entries by a full name.
        /// </summary>
        /// <remarks>
        /// Only applicable in tests, because even with V2 semantic they still use 'by name' value resolution.
        /// </remarks>
        bool? PreserveFullNames { get; }

        /// <summary>
        /// Disables cycle detection if set to true.
        /// </summary>
        bool? DisableCycleDetection { get; }

        /// <summary>
        /// Whether the parser preserves trivia. This is not intended to be exposed to the user and it is used for internal tools.
        /// </summary>
        bool? PreserveTrivia { get; }

        /// <summary>
        /// Front end will stop parsing, binding or type checking if the error count exceeds the limit.
        /// </summary>
        /// <remarks>
        /// In some cases a build cone could have so many errors that even printing them on the screen will take too long.
        /// This option allows a user to stop the execution when error limit is reached.
        /// </remarks>
        int? ErrorLimit { get; }

        /// <summary>
        /// Forces downloaded packages to be republished to the cache
        /// </summary>
        bool? ForcePopulatePackageCache { get; }

        /// <summary>
        /// If true, all the nuget packages should be materialized on disk and the error will happen otherwise.
        /// </summary>
        /// <remarks>
        /// In some cases (mac builds specifically) we want can't restore nuget packages and should rely on the manual restore process.
        /// This flag enforces this behavior and gracefully fails when the packages are not available already.
        /// </remarks>
        bool? UsePackagesFromFileSystem { get; }

        /// <summary>
        /// If true the package from disk will be reused only when the fingerpint matches.
        /// </summary>
        /// <remarks>
        /// False by default.
        /// </remarks>
        bool? RespectWeakFingerprintForNugetUpToDateCheck { get; }

        /// <summary>
        /// Office-specific flag. Office passes this so we can ensure that when V2 becomes the default, office maintains existing codepath via this flag if needed.
        /// </summary>
        /// TODO: remove this when Office is in DScript V2
        bool? UseLegacyOfficeLogic { get; }

        /// <summary>
        /// If specified all method invocations will be tracked and top N most frequently methods will be captured in the log.
        /// </summary>
        bool? TrackMethodInvocations { get; set; }

        /// <summary>
        /// Suggested waiting time before starting the cycle detection in seconds.
        /// </summary>
        int? CycleDetectorStartupDelay { get; set; }

        /// <summary>
        /// If specified, then BuildXL will fail if workspace-related memory is not collected successfully
        /// before evalution phase.
        /// </summary>
        /// <remarks>
        /// This is temporary flag to unblock rolling builds.
        /// </remarks>
        bool? FailIfWorkspaceMemoryIsNotCollected { get; set; }

        /// <summary>
        /// If specified, then BuildXL will fail if frontend-related memory is not collected successfully
        /// when the front end has been used
        /// </summary>
        bool? FailIfFrontendMemoryIsNotCollected { get; set; }

        /// <summary>
        /// Attempt to reload previous graph and use "PatchableGraphBuilder" which allows
        /// the front end to patch the graph based on changed spec files.
        /// </summary>
        bool? UseGraphPatching { get; }

        /// <summary>
        /// Attempt to reload previous engine state (path and symbol tables, and graph). This is a precondition for UseGraphPatching
        /// but also for other optimizations that need the same tables across builds
        /// </summary>
        bool? ReloadPartialEngineStateWhenPossible { get; }

        /// <summary>
        /// Whether parsing is cancelled when the first spec fails to parse
        /// </summary>
        /// <remarks>
        /// There is not a command-line option for this, and this is not really intended to be user facing (even though
        /// if a user decides to set this to false, it will work). This flag is mainly for the IDE to configure the engine.
        /// </remarks>
        bool? CancelParsingOnFirstFailure { get; set; }

        /// <summary>
        /// Whether the public surface of specs and serialized AST are attempted to be used when available
        /// </summary>
        bool? UseSpecPublicFacadeAndAstWhenAvailable { get; set; }

        /// <summary>
        /// Whether to cancel any pending evaluation after first error.
        /// </summary>
        /// <remarks>
        /// Currently, there is no separate command-line option for this; instead, the existing /stopOnFirstError switch
        /// is used to control both <see cref="IScheduleConfiguration"/> and this option.
        /// </remarks>
        bool? CancelEvaluationOnFirstFailure { get; set; }

        /// <summary>
        /// Enables module configuration files to declare a list of cyclical friend modules.
        /// </summary>
        bool? EnableCyclicalFriendModules { get; set; }

        /// <summary>
        /// Whether the main log file should contain statistics for the frontend.
        /// </summary>
        bool LogStatistics { get; }

        /// <summary>
        /// Whether the frontend statistics should contain statistics about the slowest proccesses. 
        /// </summary>
        bool ShowSlowestElementsStatistics { get; }

        /// <summary>
        /// Whether the frontend statistics should contain statistics about the largest files.
        /// </summary>
        bool ShowLargestFilesStatistics { get; }

        /// <summary> 
        /// Environment Variables which should be passed through for all processes 
        /// </summary> 
        /// <remarks>
        /// This is an unsafe configuration.
        /// This global configuration from cammand line will bypass cache,
        /// which means pips and graph will be cached ignoring environment variables specified in this configure
        /// </remarks>
        IReadOnlyList<string> GlobalUnsafePassthroughEnvironmentVariables { get; }
    }
}
