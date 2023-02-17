// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <summary>
    /// Mutable front-end configuration.
    /// </summary>
    public sealed class FrontEndConfiguration : IFrontEndConfiguration
    {
        /// <nodoc />
        public FrontEndConfiguration()
        {
            // Need to initialize explicitly to avoid contract violation.
            EnabledPolicyRules = new List<string>();
            LogStatistics = true;
            ReleaseWorkspaceBeforeEvaluation = true;
            CredScanEnvironmentVariablesAllowList = new List<string>();
        }

        /// <nodoc />
        public FrontEndConfiguration(IFrontEndConfiguration template, PathRemapper pathRemapper)
        {
            Contract.Requires(template != null);
            Contract.Assume(pathRemapper != null);

            ProfileScript = template.ProfileScript;
            ProfileReportDestination = pathRemapper.Remap(template.ProfileReportDestination);
            FileToFileReportDestination = pathRemapper.Remap(template.FileToFileReportDestination);

            EnableIncrementalFrontEnd = template.EnableIncrementalFrontEnd;
            EnableEvaluationThrottling = template.EnableEvaluationThrottling; 
            DebugScript = template.DebugScript;
            DebuggerBreakOnExit = template.DebuggerBreakOnExit;
            DebuggerPort = template.DebuggerPort;
            UsePartialEvaluation = template.UsePartialEvaluation;
            EnabledPolicyRules = new List<string>(template.EnabledPolicyRules);
            MaxFrontEndConcurrency = template.MaxFrontEndConcurrency;
            MaxRestoreNugetConcurrency = template.MaxRestoreNugetConcurrency;
            ThreadPoolMinThreadCountMultiplier = template.ThreadPoolMinThreadCountMultiplier;
            MaxTypeCheckingConcurrency = template.MaxTypeCheckingConcurrency;
            DisableLanguagePolicyAnalysis = template.DisableLanguagePolicyAnalysis;
            DisableIsObsoleteCheckDuringConversion = template.DisableIsObsoleteCheckDuringConversion;
            NameResolutionSemantics = template.NameResolutionSemantics;
            PreserveFullNames = template.PreserveFullNames;
            DisableCycleDetection = template.DisableCycleDetection;
            PreserveTrivia = template.PreserveTrivia;
            ErrorLimit = template.ErrorLimit;
            ForcePopulatePackageCache = template.ForcePopulatePackageCache;
            UsePackagesFromFileSystem = template.UsePackagesFromFileSystem;
            RespectWeakFingerprintForNugetUpToDateCheck = template.RespectWeakFingerprintForNugetUpToDateCheck;
            ForceGenerateNuGetSpecs = template.ForceGenerateNuGetSpecs;
            UseLegacyOfficeLogic = template.UseLegacyOfficeLogic;
            TrackMethodInvocations = template.TrackMethodInvocations;
            CycleDetectorStartupDelay = template.CycleDetectorStartupDelay;
            FailIfWorkspaceMemoryIsNotCollected = template.FailIfWorkspaceMemoryIsNotCollected;
            FailIfFrontendMemoryIsNotCollected = template.FailIfFrontendMemoryIsNotCollected;
            ConstructAndSaveBindingFingerprint = template.ConstructAndSaveBindingFingerprint;
            UseGraphPatching = template.UseGraphPatching;
            CancelParsingOnFirstFailure = template.CancelParsingOnFirstFailure;
            UseSpecPublicFacadeAndAstWhenAvailable = template.UseSpecPublicFacadeAndAstWhenAvailable;
            CancelEvaluationOnFirstFailure = template.CancelEvaluationOnFirstFailure;
            ReloadPartialEngineStateWhenPossible = template.ReloadPartialEngineStateWhenPossible;
            EnableCyclicalFriendModules = template.EnableCyclicalFriendModules;
            LogStatistics = template.LogStatistics;
            ShowSlowestElementsStatistics = template.ShowSlowestElementsStatistics;
            ShowLargestFilesStatistics = template.ShowLargestFilesStatistics;
            ReleaseWorkspaceBeforeEvaluation = template.ReleaseWorkspaceBeforeEvaluation;
            UnsafeOptimizedAstConversion = template.UnsafeOptimizedAstConversion;
            AllowUnsafeAmbient = template.AllowUnsafeAmbient;
            GenerateCgManifestForNugets = template.GenerateCgManifestForNugets;
            ValidateCgManifestForNugets = template.ValidateCgManifestForNugets;
            AllowMissingSpecs = template.AllowMissingSpecs;
            EnableCredScan = template.EnableCredScan;
            CredScanEnvironmentVariablesAllowList = new List<string>(template.CredScanEnvironmentVariablesAllowList);
        }

        /// <inheritdoc />
        public bool? EnableIncrementalFrontEnd { get; set; }

        /// <inheritdoc />
        public bool? ProfileScript { get; set; }

        /// <inheritdoc />
        public AbsolutePath? ProfileReportDestination { get; set; }

        /// <inheritdoc />
        public AbsolutePath? FileToFileReportDestination { get; set; }

        /// <inheritdoc />
        public bool? DebugScript { get; set; }

        /// <inheritdoc />
        public bool? DebuggerBreakOnExit { get; set; }

        /// <inheritdoc />
        public int? DebuggerPort { get; set; }

        /// <inheritdoc />
        public int? MaxFrontEndConcurrency { get; set; }

        /// <inheritdoc />
        public bool? EnableEvaluationThrottling { get; set; }

        /// <inheritdoc />
        public int? MaxRestoreNugetConcurrency { get; set; }

        /// <inheritdoc />
        public int? ThreadPoolMinThreadCountMultiplier { get; set; }

        /// <inheritdoc />
        public int? MaxTypeCheckingConcurrency { get; set; }

        /// <inheritdoc />
        public bool? UsePartialEvaluation { get; set; }

        /// <summary>Maximum number of loop iterations to execute before reporting "loop overflow" error.</summary>
        public int? MaxLoopIterations { get; set; }

        /// <summary>
        /// Set of "linter" rule names that should be applied during the build.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<string> EnabledPolicyRules { get; set; }

        /// <inheritdoc />
        IReadOnlyList<string> IFrontEndConfiguration.EnabledPolicyRules => EnabledPolicyRules;

        /// <inheritdoc />
        public bool? DisableLanguagePolicyAnalysis { get; set; }

        /// <inheritdoc />
        public bool? DisableIsObsoleteCheckDuringConversion { get; set; }

        /// <inheritdoc/>
        public NameResolutionSemantics? NameResolutionSemantics { get; set; }

        /// <inheritdoc/>
        public bool? PreserveFullNames { get; set; }

        /// <inheritdoc/>
        public bool? DisableCycleDetection { get; set; }

        /// <inheritdoc/>
        public bool? PreserveTrivia { get; set; }

        /// <inheritdoc />
        public int? ErrorLimit { get; set; }

        /// <inheritdoc/>
        public bool? ForcePopulatePackageCache { get; set; }

        /// <inheritdoc/>
        public bool? UsePackagesFromFileSystem { get; set; }

        /// <inheritdoc/>
        public bool? RespectWeakFingerprintForNugetUpToDateCheck { get; set; }

        /// <inheritdoc/>
        public bool? ForceGenerateNuGetSpecs { get; set; }

        /// <inheritdoc/>
        public bool? UseLegacyOfficeLogic { get; set; }

        /// <inheritdoc/>
        public bool? TrackMethodInvocations { get; set; }

        /// <inheritdoc/>
        public int? CycleDetectorStartupDelay { get; set; }

        /// <inheritdoc/>
        public bool? FailIfWorkspaceMemoryIsNotCollected { get; set; }

        /// <inheritdoc/>
        public bool? FailIfFrontendMemoryIsNotCollected { get; set; }

        /// <inheritdoc/>
        public bool? ConstructAndSaveBindingFingerprint { get; set; }

        /// <inheritdoc/>
        public bool? UseGraphPatching { get; set; }

        /// <inheritdoc/>
        public bool? ReloadPartialEngineStateWhenPossible { get; set; }

        /// <inheritdoc/>
        public bool? CancelParsingOnFirstFailure { get; set; }

        /// <inheritdoc/>
        public bool? UseSpecPublicFacadeAndAstWhenAvailable { get; set; }

        /// <inheritdoc/>
        public bool? CancelEvaluationOnFirstFailure { get; set; }

        /// <inheritdoc/>
        public bool? EnableCyclicalFriendModules { get; set; }

        /// <inheritdoc/>
        public bool LogStatistics { get; set; }

        /// <inheritdoc/>
        public bool ShowSlowestElementsStatistics { get; set; }

        /// <inheritdoc/>
        public bool ShowLargestFilesStatistics { get; set; }

        /// <inheritdoc /> 
        public bool ReleaseWorkspaceBeforeEvaluation { get; set; }

        /// <inheritdoc /> 
        public bool UnsafeOptimizedAstConversion { get; set; }

        /// <inheritdoc /> 
        public bool AllowUnsafeAmbient { get; set; }

        /// <inheritdoc />
        public AbsolutePath GenerateCgManifestForNugets { get; set; }

        /// <inheritdoc />
        public AbsolutePath ValidateCgManifestForNugets { get; set; }

        /// <inheritdoc />
        public bool? AllowMissingSpecs { get; set; }

        /// <inheritdoc />
        public bool? EnableCredScan { get; set; }

        /// <nodoc />
        public List<string> CredScanEnvironmentVariablesAllowList { get; set; }

        /// <inheritdoc />
        IReadOnlyList<string> IFrontEndConfiguration.CredScanEnvironmentVariablesAllowList => CredScanEnvironmentVariablesAllowList;
    }
}
