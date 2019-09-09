// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.Utilities.Configuration.Mutable;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Set of extension methods for <see cref="IFrontEndConfiguration"/>.
    /// </summary>
    public static class FrontEndConfigurationExtensions
    {
        // Defaults

        /// <nodoc/>
        public const bool DefaultEnableIncrementalFrontEnd = false;

        /// <nodoc/>
        public const bool DefaultProfileScript = false;

        /// <nodoc/>
        public const string DefaultProfileReportFilename = "ScriptProfile.tsv";

        /// <nodoc/>
        public const bool DefaultDebugScript = false;

        /// <nodoc/>
        public const bool DefaultDebuggerBreakOnExit = false;

        /// <nodoc/>
        public const bool DefaultForcePopulatePackageCache = false;

        /// <nodoc/>
        public const bool DefaultRespectWeakFingerprint = false;

        /// <nodoc/>
        public const int DefaultDebuggerPort = 41177;

        /// <nodoc/>
        public const bool DefaultTypeCheck = false;

        /// <nodoc/>
        public const bool DefaultDisableLanguagePolicyAnalysis = false;
        
        /// <nodoc/>
        public const bool DefaultPreserveFullNames = false;

        /// <nodoc/>
        public const bool DefaultDisableCycleDetection = false;

        /// <nodoc/>
        public const bool DefaultPreserveTrivia = false;

        /// <nodoc/>
        public const int DefaultErrorLimit = 1000;

        /// <nodoc/>
        public const bool DefaultUseLegacyOfficeLogic = false;

        /// <nodoc/>
        public const bool DefaultTrackMethodInvocationCount = false;

        /// <nodoc/>
        public const int DefaultCycleDetectorStartupDelay = 10;

        /// <nodoc />
        public const bool DefaultFailIfWorkspaceMemoryIsNotCollected = false;

        /// <nodoc />
        public const bool DefaultFailIfFrontendMemoryIsNotCollected = false;

        /// <nodoc />
        public const bool DefaultCancelParsingOnFirstFailure = true;

        /// <nodoc />
        public const bool DefaultConstructAndSaveBindingFingerprint = true;

        /// <nodoc />
        public const bool DefaultCancelEvaluationOnFirstFailure = false;

        /// <nodoc />
        public const bool DefaultEnableCyclicalFriendModules = false;

        /// <nodoc/>
        public static AbsolutePath DefaultProfileReportDestination(PathTable pathTable) => AbsolutePath.Create(pathTable, Environment.CurrentDirectory).Combine(pathTable, DefaultProfileReportFilename);

        /// <nodoc/>
        public static readonly int DefaultMaxFrontEndConcurrency = Math.Max(Environment.ProcessorCount, 256 / Environment.ProcessorCount); // the more cores, the lower the threshold.

        /// <nodoc/>
        public static readonly bool DefaultEnableEvaluationThrottling =
#if FEATURE_THROTTLE_EVAL_SCHEDULER
            true;
#else
            false;
#endif

        /// <nodoc/>
        public static readonly int DefaultintThreadPoolMinThreadCountMultiplier = 3;

        /// <nodoc/>
        public static readonly NameResolutionSemantics DefaultExplicitProjectReferences = Configuration.NameResolutionSemantics.ImplicitProjectReferences;

        /// <summary>
        /// Limits the number of loop iterations.
        /// </summary>
        public static int DefaultIterationThreshold = 10000000;

        // Extension methods

        /// <nodoc/>
        public static bool ProfileScript(this IFrontEndConfiguration configuration) => 
            configuration.ProfileScript ?? DefaultProfileScript;

        /// <nodoc/>
        public static AbsolutePath ProfileReportDestination(this IFrontEndConfiguration configuration, PathTable pathTable) => 
            configuration.ProfileReportDestination ?? DefaultProfileReportDestination(pathTable);

        /// <nodoc/>
        public static FrontEndMode FrontEndMode(this IFrontEndConfiguration configuration)
        {
            if (configuration.DebugScript())
            {
                return Configuration.FrontEndMode.DebugScript;
            }

            if (configuration.ProfileScript())
            {
                return Configuration.FrontEndMode.ProfileScript;
            }

            return Configuration.FrontEndMode.NormalMode;
        }

        /// <nodoc/>
        public static bool DebugScript(this IFrontEndConfiguration configuration) => 
            configuration.DebugScript ?? DefaultDebugScript;

        /// <nodoc/>
        public static bool ForcePopulatePackageCache(this IFrontEndConfiguration configuration) => 
            configuration.ForcePopulatePackageCache ?? DefaultForcePopulatePackageCache;

        /// <nodoc/>
        public static bool UsePackagesFromFileSystem(this IFrontEndConfiguration configuration) => 
            configuration.UsePackagesFromFileSystem ?? false;

        /// <nodoc/>
        public static bool RespectWeakFingerprintForNugetUpToDateCheck(this IFrontEndConfiguration configuration) => 
            configuration.RespectWeakFingerprintForNugetUpToDateCheck ?? DefaultRespectWeakFingerprint;

        /// <nodoc/>
        public static bool DebuggerBreakOnExit(this IFrontEndConfiguration configuration) => 
            configuration.DebuggerBreakOnExit ?? DefaultDebuggerBreakOnExit;

        /// <nodoc/>
        public static int DebuggerPort(this IFrontEndConfiguration configuration) => 
            configuration.DebuggerPort ?? DefaultDebuggerPort;

        /// <nodoc/>
        public static int MaxFrontEndConcurrency(this IFrontEndConfiguration configuration) => 
            configuration.MaxFrontEndConcurrency ?? DefaultMaxFrontEndConcurrency;

        /// <nodoc/>
        public static bool EnableEvaluationThrottling(this IFrontEndConfiguration configuration) =>
            configuration.EnableEvaluationThrottling ?? DefaultEnableEvaluationThrottling;

        /// <nodoc/>
        public static int MaxRestoreNugetConcurrency(this IFrontEndConfiguration configuration) => 
            configuration.MaxRestoreNugetConcurrency ?? MaxFrontEndConcurrency(configuration);

        /// <nodoc/>
        public static int ThreadPoolMinThreadCountMultiplier(this IFrontEndConfiguration configuration)
            => configuration.ThreadPoolMinThreadCountMultiplier ?? DefaultintThreadPoolMinThreadCountMultiplier;

        /// <nodoc/>
        public static int MaxTypeCheckingConcurrency(this IFrontEndConfiguration configuration) => 
            configuration.MaxTypeCheckingConcurrency ?? configuration.MaxFrontEndConcurrency();

        /// <nodoc/>
        public static bool UsePartialEvaluation(this IFrontEndConfiguration configuration) => 
            configuration.UsePartialEvaluation ?? true;

        /// <nodoc />
        public static bool UseOfficeBackCompatPreludeHacks(this IFrontEndConfiguration configuration)
        {
            if (configuration.EnabledPolicyRules != null && configuration.EnabledPolicyRules.Contains("NoTransformers"))
            {
                // When notransformer policy is present this is always off.
                return false;
            }

            // Else when running office it is on, and defaults to false.
            return configuration.UseLegacyOfficeLogic ?? false;
        }

        /// <nodoc />
        public static bool EnableIncrementalFrontEnd(this IFrontEndConfiguration configuration) =>
            configuration.EnableIncrementalFrontEnd ?? DefaultEnableIncrementalFrontEnd;

        /// <nodoc />
        public static bool ConstructAndSaveBindingFingerprint(this IFrontEndConfiguration configuration) =>
            EnableIncrementalFrontEnd(configuration) &&
            (configuration.ConstructAndSaveBindingFingerprint ?? DefaultConstructAndSaveBindingFingerprint);

        /// <summary>
        /// Returns true if spec-2-spec map should be constructed and saved during semantic analysis.
        /// </summary>
        public static bool TrackFileToFileDependencies(this IFrontEndConfiguration configuration) =>
            configuration.EnableIncrementalFrontEnd() && (configuration.ConstructAndSaveBindingFingerprint() || configuration.UsePartialEvaluation());

        /// <nodoc />
        public static bool UseSpecPublicFacadeAndAstWhenAvailable(this IFrontEndConfiguration configuration)
        {
            if (!configuration.EnableIncrementalFrontEnd())
            {
                // If incremental front-end is disabled, then it make no sense to construct public facades.
                return false;
            }

            if (configuration.UseSpecPublicFacadeAndAstWhenAvailable != null)
            {
                return configuration.UseSpecPublicFacadeAndAstWhenAvailable.Value;
            }

            // This optimization is on by default;
            return true;
        }

        /// <nodoc/>
        public static bool DisableLanguagePolicyAnalysis(this IFrontEndConfiguration configuration) => 
            configuration.DisableLanguagePolicyAnalysis ?? DefaultDisableLanguagePolicyAnalysis;

        /// <nodoc/>
        public static NameResolutionSemantics NameResolutionSemantics(this IFrontEndConfiguration configuration) =>
            configuration.NameResolutionSemantics ?? DefaultExplicitProjectReferences;

        /// <nodoc/>
        public static bool PreserveFullNames(this IFrontEndConfiguration configuration) =>
            configuration.PreserveFullNames ?? DefaultPreserveFullNames;

        /// <nodoc/>
        public static bool DisableCycleDetection(this IFrontEndConfiguration configuration) =>
            configuration.DisableCycleDetection ?? DefaultDisableCycleDetection;

        /// <nodoc/>
        public static bool PreserveTrivia(this IFrontEndConfiguration configuration) =>
            configuration.PreserveTrivia ?? DefaultPreserveTrivia;

        /// <nodoc />
        public static int ErrorLimit(this IFrontEndConfiguration configuration) => 
            configuration.ErrorLimit ?? DefaultErrorLimit;

        /// <nodoc />
        public static bool UseLegacyOfficeLogic(this IFrontEndConfiguration configuration) =>
            configuration.UseLegacyOfficeLogic ?? DefaultUseLegacyOfficeLogic;

        /// <nodoc />
        public static bool TrackMethodInvocations(this IFrontEndConfiguration configuration) =>
            configuration.TrackMethodInvocations ?? DefaultTrackMethodInvocationCount;

        /// <nodoc />
        public static int CycleDetectorStartupDelay(this IFrontEndConfiguration configuration) =>
            configuration.CycleDetectorStartupDelay ?? DefaultCycleDetectorStartupDelay;

        /// <nodoc />
        public static bool FailIfWorkspaceMemoryIsNotCollected(this IFrontEndConfiguration configuration) =>
            configuration.FailIfWorkspaceMemoryIsNotCollected ?? DefaultFailIfWorkspaceMemoryIsNotCollected;

        /// <nodoc />
        public static bool FailIfFrontendMemoryIsNotCollected(this IFrontEndConfiguration configuration) =>
            configuration.FailIfFrontendMemoryIsNotCollected ?? DefaultFailIfFrontendMemoryIsNotCollected;

        /// <nodoc />
        public static bool UseGraphPatching(this IFrontEndConfiguration configuration) =>
            configuration.UseGraphPatching == true;

        /// <nodoc />
        public static bool CancelParsingOnFirstFailure(this IFrontEndConfiguration configuration) =>
            configuration.CancelParsingOnFirstFailure ?? DefaultCancelParsingOnFirstFailure;

        /// <nodoc />
        public static bool CancelEvaluationOnFirstFailure(this IFrontEndConfiguration configuration) =>
            configuration.CancelEvaluationOnFirstFailure ?? DefaultCancelEvaluationOnFirstFailure;

        /// <nodoc />
        public static bool ReloadPartialEngineStateWhenPossible(this IFrontEndConfiguration configuration)
        {
            if (configuration.ReloadPartialEngineStateWhenPossible != null)
            {
                return configuration.ReloadPartialEngineStateWhenPossible.Value;
            }

            // This optimization is on when incremental front end is not explicitly disabled
            return configuration.EnableIncrementalFrontEnd != false;
        }

        /// <nodoc />
        public static bool EnableCyclicalFriendModules(this IFrontEndConfiguration configuration) =>
            configuration.EnableCyclicalFriendModules ?? DefaultEnableCyclicalFriendModules;

        /// <summary>
        /// Only unit test can specify MaxloopIterations to override this setting.
        /// </summary>
        public static int MaxLoopIterations(this IFrontEndConfiguration configuration) =>
            (configuration as FrontEndConfiguration)?.MaxLoopIterations ?? DefaultIterationThreshold;
    }
}
