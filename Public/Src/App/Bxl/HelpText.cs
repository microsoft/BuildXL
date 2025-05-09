// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using HelpLevel = BuildXL.ToolSupport.HelpLevel;
using Strings = bxl.Strings;

#pragma warning disable SA1123 // Region should not be located within a code element.

namespace BuildXL
{
    internal static class HelpText
    {
        public static void DisplayLogo()
        {
            var hw = new HelpWriter();

            hw.WriteLine(string.Format(
                Strings.HelpText_DisplayLogo_Template,
                Branding.LongProductName,
                Branding.Version,
                Branding.SourceVersion
            ));

            hw.WriteLine(Strings.HelpText_DisplayLogo_Copyright);
            hw.WriteLine();
        }

        public static void DisplayHelp(HelpLevel helpLevel)
        {
            var hw = new HelpWriter(helpLevel);

            hw.WriteBanner(Strings.HelpText_DisplayHelp_BuildBanner);

            #region Build
            hw.WriteOption(
                "/config:<file>",
                Strings.HelpText_DisplayHelp_Config);

            hw.WriteOption(
                "/additionalConfigFile:<file>*",
                Strings.HelpText_DisplayHelp_AdditionalConfigFile,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/qualifier:<qualifier list>",
                Strings.HelpText_DisplayHelp_Qualifier);

            hw.WriteOption(
                "/objectDirectory:<output directory>",
                Strings.HelpText_DisplayHelp_ObjectDirectory);

            hw.WriteOption(
                "/tempDirectory:<temp directory>",
                Strings.HelpText_DisplayHelp_TempDirectory,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/property:<key>=<value>",
                Strings.HelpText_DisplayHelp_Property);

            hw.WriteOption(
                "<paths>",
                Strings.HelpText_DisplayHelp_Paths);
            #endregion

            hw.WriteBanner(Strings.HelpText_DisplayHelp_FilteringBanner);

            #region Filtering
            hw.WriteLine(Strings.HelpText_DisplayHelp_FilteringInfo);
            hw.WriteLine();

            hw.WriteOption(
                "/filter:<deps><filter>",
                Strings.HelpText_DisplayHelp_Filter);

            hw.WriteOption(
                "<deps>",
                Strings.HelpText_DisplayHelp_DependencySelection,
                HelpLevel.Verbose);

            hw.WriteOption(
                "<filter>",
                Strings.HelpText_DisplayHelp_Filter_Expression,
                HelpLevel.Verbose);

            hw.WriteLine(Strings.HelpText_DisplayHelp_FilterTypeExplanation, HelpLevel.Verbose);
            hw.WriteLine(HelpLevel.Verbose);

            hw.WriteOption(
                "id",
                Strings.HelpText_DisplayHelp_Filter_Id,
                HelpLevel.Verbose);

            hw.WriteOption(
                "output",
                Strings.HelpText_DisplayHelp_Filter_Output + " " +
                Strings.HelpText_DisplayHelp_Filter_PathArgument,
                HelpLevel.Verbose);

            hw.WriteOption(
                "input",
                Strings.HelpText_DisplayHelp_Filter_Input + " " +
                Strings.HelpText_DisplayHelp_Filter_PathArgument,
                HelpLevel.Verbose);

            hw.WriteOption(
                "tag",
                Strings.HelpText_DisplayHelp_Filter_Tag,
                HelpLevel.Verbose);

            hw.WriteOption(
                "value",
                Strings.HelpText_DisplayHelp_Filter_Value,
                HelpLevel.Verbose);
            hw.WriteOption(
                "valuetransitive",
                Strings.HelpText_DisplayHelp_Filter_ValueTransitive,
                HelpLevel.Verbose);

            hw.WriteOption(
                "spec",
                Strings.HelpText_DisplayHelp_Filter_Spec + " " +
                Strings.HelpText_DisplayHelp_Filter_PathArgument,
                HelpLevel.Verbose);
            hw.WriteOption(
                "spec_valuetransitive",
                Strings.HelpText_DisplayHelp_Filter_Spec_ValueTransitive,
                HelpLevel.Verbose);
            hw.WriteOption(
                "specref",
                Strings.HelpText_DisplayHelp_Filter_SpecDependencies,
                HelpLevel.Verbose);

            hw.WriteLine(Strings.HelpText_DisplayHelp_FilterFunctionExplanation, HelpLevel.Verbose);
            hw.WriteOption("dpt",
                Strings.HelpText_DisplayHelp_Filter_DependentsFunction,
                HelpLevel.Verbose);
            hw.WriteOption("dpc",
                Strings.HelpText_DisplayHelp_Filter_DependenciesFunction,
                HelpLevel.Verbose);
            hw.WriteOption("copydpt",
                Strings.HelpText_DisplayHelp_Filter_CopyDependentsFunction,
                HelpLevel.Verbose);
            hw.WriteOption("requiredfor",
                Strings.HelpText_DisplayHelp_Filter_RequiredInputsFunction,
                HelpLevel.Verbose);
            hw.WriteLine(HelpLevel.Verbose);

            hw.WriteLine(Strings.HelpText_DisplayHelp_Filter_Examples,
                HelpLevel.Verbose);

            // Note to maintainer: if you're changing one of those samples,
            // make sure that they're valid by testing them in FilterParserTests.
            hw.WriteOption(
                "  /f:~(tag='test')",
                Strings.HelpText_DisplayHelp_FilterExampleNoTest,
                HelpLevel.Verbose);
            hw.WriteOption(
                "  /f:+spec='src\\utilities\\*'",
                Strings.HelpText_DisplayHelp_FilterExampleDirectory,
                HelpLevel.Verbose);
            hw.WriteOption(
                "  /f:(tag='csc.exe'and~(tag='test'))",
                Strings.HelpText_DisplayHelp_Filter_ExamplesBinaryFilter,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/canonicalizeFilterOutputs[+|-]",
                Strings.HelpText_DisplayHelp_CanonicalizeFilterOutputs);

            #endregion

            hw.WriteBanner(Strings.HelpText_DisplayHelp_LoggingBanner);

            #region Logging

            hw.WriteOption(
                "/maxNumPipTelemetryBatches:<int>",
                Strings.HelpText_DisplayHelp_MaxNumPipTelemetryBatches);

            hw.WriteOption(
                "/logsDirectory:<path>",
                Strings.HelpText_DisplayHelp_LogsDirectory);

            hw.WriteOption(
                "/logPrefix:<string>",
                Strings.HelpText_DisplayHelp_LogPrefix,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/logsToRetain:<number>",
                Strings.HelpText_DisplayHelp_LogsToRetain,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/customLog:<file>=<id list>",
                Strings.HelpText_DisplayHelp_CustomLog,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/logToKusto",
                Strings.HelpText_DisplayHelp_LogToKusto,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/logToKustoBlobUri:<uri>",
                Strings.HelpText_DisplayHelp_LogToKustoBlobUri,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/logToKustoIdentityId:<identityId>",
                Strings.HelpText_DisplayHelp_LogToKustoIdentityId,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/noLog:<id list>",
                Strings.HelpText_DisplayHelp_NoLog,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/forwardWorkerLog:<id list>",
                Strings.HelpText_DisplayHelp_ForwardWorkerLog,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/consoleVerbosity:<level>",
                Strings.HelpText_DisplayHelp_ConsoleVerbosity,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/fileVerbosity:<level>",
                Strings.HelpText_DisplayHelp_FileVerbosity,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/logExecution[+|-]",
                Strings.HelpText_DisplayHelp_LogExecution,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/noExecutionLog:<id list>",
                Strings.HelpText_DisplayHelp_NoExecutionLog,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/logProcesses[+|-]",
                Strings.HelpText_DisplayHelp_LogProcesses,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/logProcessData[+|-]",
                Strings.HelpText_DisplayHelp_LogProcessData,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/logFileAccessTables[+|-]",
                Strings.HelpText_DisplayHelp_LogFileAccessTables,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/logObservedFileAccesses[+|-]",
                Strings.HelpText_DisplayHelp_LogObservedFileAccesses,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/logOutput:<option>",
                Strings.HelpText_DisplayHelp_LogOutput,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/environment:<environment>",
                string.Format(
                    CultureInfo.InvariantCulture,
                    Strings.HelpText_DisplayHelp_Environment, string.Join(",", Enum.GetNames(typeof(BuildXL.Utilities.Configuration.ExecutionEnvironment)))),
                HelpLevel.Verbose);

            hw.WriteOption(
                "/remoteTelemetry[+|-]",
                Strings.HelpText_DisplayHelp_RemoteTelemetry);

            hw.WriteOption(
                "/replayWarnings[+|-]",
                Strings.HelpText_DisplayHelp_ReplayWarnings);

            hw.WriteOption(
                "/traceInfo:<Key>=<Value>",
                Strings.HelpText_DisplayHelp_TraceInfo,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/logMemory[+|-]",
                Strings.HelpText_DisplayHelp_LogMemory,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/storeFingerprints[+|-]",
                Strings.HelpText_DisplayHelp_StoreFingerprints,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/storeFingerprints:<Default|ExecutionFingerprintsOnly|IgnoreExistingEntries>",
                Strings.HelpText_DisplayHelp_StoreFingerprintsWithMode,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/substSource:[path]",
                Strings.HelpText_DisplayHelp_SubstSource,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/substTarget:[path]",
                Strings.HelpText_DisplayHelp_SubstTarget,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/fancyConsole[+|-]",
                Strings.HelpText_DisplayHelp_FancyConsole,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/fancyConsoleMaxStatusPips:<number>",
                Strings.HelpText_DisplayHelp_FancyConsoleMaxStatusPips,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/profileScript[+|-]",
                Strings.HelpText_DisplayHelp_ProfileScript,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/profileReportDestination:[path]",
                string.Format(
                    CultureInfo.InvariantCulture,
                    Strings.HelpText_DisplayHelp_ProfileReportDestination,
                    FrontEndConfigurationExtensions.DefaultProfileReportFilename),
                HelpLevel.Verbose);
            hw.WriteOption(
                "/trackBuildsInUserFolder[+-]",
                Strings.HelpText_DisplayHelp_TrackBuildsInUserFolder,
                HelpLevel.Verbose
                );
            hw.WriteOption(
                "/trackGvfsProjections[+-]",
                Strings.HelpText_DisplayHelp_TrackGvfsProjections,
                HelpLevel.Verbose
                );
            hw.WriteOption(
                "/trackMethodInvocations[+|-]",
                string.Format(
                    CultureInfo.InvariantCulture,
                    Strings.HelpText_DisplayHelp_TrackMethodInvocations),
                HelpLevel.Verbose);

            hw.WriteOption(
                "/useCustomPipDescriptionOnConsole[+|-]",
                Strings.HelpText_DisplayHelp_UseCustomPipDescriptionOnConsole,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/cacheMiss[+|-|<path>|{<changeset list}]",
                Strings.HelpText_DisplayHelp_CacheMiss,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/cacheMissDiffFormat:[format]",
                Strings.HelpText_DisplayHelp_CacheMissDiffFormat,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/scriptShowSlowest[+|-]",
                Strings.HelpText_DisplayHelp_ScriptShowSlowest,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/scriptShowLargest[+|-]",
                Strings.HelpText_DisplayHelp_ScriptShowLargest,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/dumpFailedPips[+|-]",
                Strings.HelpText_DisplayHelp_DumpFailedPips);

            hw.WriteOption(
                "/dumpFailedPipsWithDynamicData[+|-]",
                Strings.HelpText_DisplayHelp_DumpFailedPipsWithDynamicData);

            hw.WriteOption(
                "/dumpFailedPipsLogLimit:<number>",
                Strings.HelpText_DisplayHelp_DumpFailedPipsLogLimit);

            hw.WriteOption(
                "/logToConsole:<event id list>",
                Strings.HelpText_DisplayHelp_LogToConsole,
                HelpLevel.Verbose);

            #endregion

            hw.WriteBanner(Strings.HelpText_DisplayHelp_ErrorsAndWarningsBanner);

            #region ErrorsAndWarnings
            hw.WriteOption(
                "/warnAsError[+|-]",
                Strings.HelpText_DisplayHelp_WarnAsError,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/warnAsError[+|-]:<warn list>",
                Strings.HelpText_DisplayHelp_WarnAsErrorWithList,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/noWarn:<warn list>",
                Strings.HelpText_DisplayHelp_NoWarn,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/stopOnFirstError[+|-]",
                Strings.HelpText_DisplayHelp_StopOnFirstError);

            hw.WriteOption(
                "/stopOnFirstInternalError[+|-]",
                Strings.HelpText_DisplayHelp_StopOnFirstInternalError);
            
            hw.WriteOption(
                "/color[+|-]",
                Strings.HelpText_DisplayHelp_Color,
                HelpLevel.Verbose);
            #endregion

            hw.WriteBanner(Strings.HelpText_DisplayHelp_CachingBanner);

            #region Caching
            hw.WriteOption(
                "/allowFetchingCachedGraphFromContentCache",
                Strings.HelpText_DisplayHelp_AllowFetchingCachedGraphFromContentCache,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/cacheConfigFilePath:<file>",
                Strings.HelpText_DisplayHelp_CacheConfigFilePath);

            hw.WriteOption(
                "/cacheDirectory:<artifact cache directory>",
                Strings.HelpText_DisplayHelp_CacheDirectory);

            hw.WriteOption(
                "/cacheSessionName:<unique session name>",
                Strings.HelpText_DisplayHelp_CacheSessionName,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/cacheGraph[+|-]",
                Strings.HelpText_DisplayHelp_CacheGraph,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/cacheSpecs[+|-]",
                Strings.HelpText_DisplayHelp_CacheSpecs,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/compressGraphFiles[+|-]",
                Strings.HelpText_DisplayHelp_CompressGraphFiles,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/engineCacheDirectory:<engine cache directory>",
                Strings.HelpText_DisplayHelp_EngineCacheDirectory,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/fingerprintSalt[value]",
                Strings.HelpText_DisplayHelp_FingerprintSalt,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/reuseEngineState[+|-]",
                Strings.HelpText_DisplayHelp_ReuseEngineState,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/reuseOutputsOnDisk[+|-]",
                Strings.HelpText_DisplayHelp_ReuseOutputsOnDisk,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/server[+|-]",
                Strings.HelpText_DisplayHelp_Server);

            hw.WriteOption(
                "/serverMaxIdleTimeInMinutes",
                Strings.HelpText_DisplayHelp_ServerMaxIdleTimeInMinutes);

            hw.WriteOption(
                "/serverDeploymentDir",
                Strings.HelpText_DisplayHelp_ServerDeploymentDir,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/storeOutputsToCache",
                Strings.HelpText_DisplayHelp_StoreOutputsToCache,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/verifyCacheLookupPin[+|-]",
                Strings.HelpText_DisplayHelp_VerifyCacheLookupPin,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/pinCachedOutputs[+|-]",
                Strings.HelpText_DisplayHelp_PinCachedOutputs,
                HelpLevel.Verbose);


            hw.WriteOption(
                "/pathSetThreshold",
                Strings.HelpText_DisplayHelp_PathSetThreshold,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/augmentingPathSetCommonalityFactor:(0, 1]",
                Strings.HelpText_DisplayHelp_AugmentingPathSetCommonalityFactor,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/pathSetAugmentationMonitoring",
                Strings.HelpText_DisplayHelp_PathSetAugmentationMonitoring,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/limitPathSetsOnCacheLookup[+|-]:<number>",
                Strings.HelpText_DisplayHelp_LimitPathSetsOnCacheLookup,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/vfsCasRoot<vfs cas root directory>",
                Strings.HelpText_DisplayHelp_VfsCasRoot,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/enableDedupChunk[+|-]",
                Strings.HelpText_DisplayHelp_EnableDedupChunk,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/honorDirectoryCasingOnDisk[+|-]",
                Strings.HelpText_DisplayHelp_HonorDirectoryCasingOnDisk,
                HelpLevel.Verbose);

            #endregion

            hw.WriteBanner(Strings.HelpText_DisplayHelp_ExecutionControlBanner);

            #region ExecutionControl
            hw.WriteOption(
                "/unsafe_AllowDuplicateTemporaryDirectory[+|-]",
                Strings.HelpText_DisplayHelp_Unsafe_AllowDuplicateTemporaryDirectory);

            hw.WriteOption(
                "/incremental[+|-]",
                Strings.HelpText_DisplayHelp_Incremental);

            hw.WriteOption(
                "/incrementalScheduling[+|-]",
                Strings.HelpText_DisplayHelp_IncrementalScheduling);

            hw.WriteOption(
                "/useHardlinks[+|-]",
                Strings.HelpText_DisplayHelp_UseHardlinks,
                HelpLevel.Verbose);

            // This feature is used to support distributed build tests internally but is not an externally supported feature.
            // Don't include it in help text
            /*
            hw.WriteOption(
                "/rootMap:<drive letter>=<path>",
                Strings.HelpText_DisplayHelp_RootMap,
                HelpLevel.Verbose);*/

            hw.WriteOption(
                "/injectCacheMisses:<rate and options>",
                Strings.HelpText_DisplayHelp_InjectCacheMisses,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/pipDefaultTimeout:<ms>",
                Strings.HelpText_DisplayHelp_PipDefaultTimeout);

            hw.WriteOption(
                "/pipDefaultWarningTimeout:<ms>",
                Strings.HelpText_DisplayHelp_PipDefaultWarningTimeout);

            hw.WriteOption(
                "/pipTimeoutMultiplier:<float>",
                Strings.HelpText_DisplayHelp_PipTimeoutMultiplier,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/pipWarningTimeoutMultiplier:<float>",
                Strings.HelpText_DisplayHelp_PipWarningTimeoutMultiplier,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/maxProc:<number of concurrent processes>",
                Strings.HelpText_DisplayHelp_MaxProc);

            hw.WriteOption(
                "/maxProcMultiplier:<double>",
                Strings.HelpText_DisplayHelp_MaxProcMultiplier,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/maxLightProc:<number of concurrent light processes>",
                Strings.HelpText_DisplayHelp_MaxLightProc);

            hw.WriteOption(
                "/maxIO:<number of concurrent I/O operations>",
                Strings.HelpText_DisplayHelp_MaxIO);

            hw.WriteOption(
                "/maxIOMultiplier:<double>",
                Strings.HelpText_DisplayHelp_MaxIOMultiplier,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/maxCacheLookup:<number of concurrent cache lookup operations>",
                Strings.HelpText_DisplayHelp_MaxCacheLookup);

            hw.WriteOption(
                "/maxChooseWorker:<number of concurrent choose worker operations>",
                Strings.HelpText_DisplayHelp_MaxChooseWorker);

            hw.WriteOption(
                "/maxChooseWorkerCacheLookup:<number of concurrent choose worker cache lookup operations>",
                Strings.HelpText_DisplayHelp_MaxChooseWorkerCacheLookup);

            hw.WriteOption(
                "/maxChooseWorkerLight:<number of concurrent choose worker operations for light pips>",
                Strings.HelpText_DisplayHelp_MaxChooseWorkerLight);

            hw.WriteOption(
                "/maxMaterialize:<number of concurrent materialize operations>",
                Strings.HelpText_DisplayHelp_MaxMaterialize);

            hw.WriteOption(
                "/lowPriority[+|-]",
                Strings.HelpText_DisplayHelp_LowPriority);

            hw.WriteOption(
                "/ramSemaphoreMultiplier:<double>",
                Strings.HelpText_DisplayHelp_RamSemaphoreMultiplier,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/deprioritizeOnSemaphoreConstraints[+|-]",
                Strings.HelpText_DisplayHelp_DeprioritizeOnSemaphoreConstraints);

            hw.WriteOption(
                "/useHistoricalCpuThrottling[+|-]",
                Strings.HelpText_DisplayHelp_UseHistoricalCpuThrottling);

            hw.WriteOption(
                "/maxCommitUtilizationPercentage:<number>",
                Strings.HelpText_DisplayHelp_MaxCommitUtilizationPercentage,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/maxRamUtilizationPercentage:<number>",
                Strings.HelpText_DisplayHelp_MaxRamUtilizationPercentage,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/minAvailableRamMb:<number>",
                Strings.HelpText_DisplayHelp_MinAvailableRamMb,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/manageMemoryMode:<memory mode>",
                Strings.HelpText_DisplayHelp_ManageMemoryMode,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/phase:<engine phase>",
                Strings.HelpText_DisplayHelp_Phase);

            hw.WriteOption(
                "/maxRelativeOutputDirectoryLength:<characters>",
                Strings.HelpText_DisplayHelp_MaxRelativeOutputDirectoryLength,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/buildLockPolling:<seconds>",
                Strings.HelpText_DisplayHelp_BuildLockPolling,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/buildTimeoutMins:<minutes>",
                Strings.HelpText_DisplayHelp_BuildTimeoutMins,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/enableLazyOutputs[+|-]",
                Strings.HelpText_DisplayHelp_EnableLazyOutputs,
                HelpLevel.Verbose);
            hw.WriteOption(
                "/cleanTempDirectories[+|-]",
                Strings.HelpText_DisplayHelp_CleanTempDirectories,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/cleanOnly[+|-]",
                Strings.HelpText_DisplayHelp_CleanOnly,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/cacheOnly[+|-]",
                Strings.HelpText_DisplayHelp_CacheOnly,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/scrub[+|-]",
                Strings.HelpText_DisplayHelp_Scrub,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/scrubDirectory:<path>",
                Strings.HelpText_DisplayHelp_ScrubDirectory,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/enforceFullReparsePointsUnderPath:<path>",
                Strings.HelpText_DisplayHelp_EnforceFullReparsePointsUnderPath,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/analyzeDependencyViolations[+|-]",
                Strings.HelpText_DisplayHelp_AnalyzeDependencyViolations,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/disableConHostSharing[+|-]",
                Strings.HelpText_DisplayHelp_DisableConHostSharing,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/scanChangeJournal[+|-]",
                Strings.HelpText_DisplayHelp_ScanChangeJournal,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/scanChangeJournalTimeLimitInSec:<seconds>",
                Strings.HelpText_DisplayHelp_ScanChangeJournalTimeLimitInSec,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/fileSystemMode<option>",
                Strings.HelpText_DisplayHelp_FileSystemMode,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/flushPageCacheToFileSystemOnStoringOutputsToCache[+|-]",
                Strings.HelpText_DisplayHelp_FlushPageCacheToFileSystemOnStoringOutputsToCache,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/unsafe_forceSkipDeps[+|-]",
                Strings.HelpText_DisplayHelp_Unsafe_ForceSkipDeps,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/normalizeReadTimestamps[+|-]",
                Strings.HelpText_DisplayHelp_NormalizeReadTimestamps,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/useLargeNtClosePreallocatedList[+|-]",
                Strings.HelpText_DisplayHelp_UseLargeNtClosePreallocatedList,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/useExtraThreadToDrainNtClose[+|-]",
                Strings.HelpText_DisplayHelp_UseExtraThreadToDrainNtClose,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/maskUntrackedAccesses[+|-]",
                Strings.HelpText_DisplayHelp_MaskUntrackedAccesses,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/treatDirectoryAsAbsentFileOnHashingInputContent[+|-]",
                Strings.HelpText_DisplayHelp_TreatDirectoryAsAbsentFileOnHashingInputContent,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/unsafe_AllowCopySymlink[+|-]",
                Strings.HelpText_DisplayHelp_Unsafe_AllowCopySymlink,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/RunInSubst[+|-]",
                Strings.HelpText_DisplayHelp_RunInSubst,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/server[+|-]",
                Strings.HelpText_DisplayHelp_Server);

            hw.WriteOption(
                "/serverDeploymentDir",
                Strings.HelpText_DisplayHelp_ServerDeploymentDir,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/processRetries:<number of retries>",
                Strings.HelpText_DisplayHelp_ProcessRetries,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/fileChangeTrackerInitializationMode:<mode>",
                Strings.HelpText_DisplayHelp_FileChangeTrackerInitializationMode,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/fileChangeTrackingExclusionRoot:<path>",
                Strings.HelpText_DisplayHelp_FileChangeTrackingExclusionRoot,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/fileChangeTrackingInclusionRoot:<path>",
                Strings.HelpText_DisplayHelp_FileChangeTrackingInclusionRoot,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/inferNonExistenceBasedOnParentPathInRealFileSystem[+|-]",
                Strings.HelpText_DisplayHelp_InferNonExistenceBasedOnParentPathInRealFileSystem,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/outputMaterializationExclusionRoot:<path>",
                Strings.HelpText_DisplayHelp_OutputMaterializationExclusionRoot,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/enforceAccessPoliciesOnDirectoryCreation[+|-]",
                Strings.HelpText_DisplayHelp_EnforceAccessPoliciesOnDirectoryCreation,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/useFileContentTable[+|-]",
                Strings.HelpText_DisplayHelp_UseFileContentTable,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/fileContentTableFile:<path>",
                Strings.HelpText_DisplayHelp_FileContentTableFile,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/fileContentTableEntryTimeToLive:<number>",
                Strings.HelpText_DisplayHelp_FileContentTableEntryTimeToLive,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/updateFileContentTableByScanningChangeJournal[+|-]",
                Strings.HelpText_DisplayHelp_UpdateFileContentTableByScanningChangeJournal,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/adminRequiredProcessExecutionMode:<mode>",
                Strings.HelpText_DisplayHelp_AdminRequiredProcessExecutionMode,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/vmConcurrencyLimit:<max number of processes executed in VM>",
                Strings.HelpText_DisplayHelp_VmConcurrencyLimit,
                HelpLevel.Verbose);

            hw.WriteOption(
                 "/adoConsoleMaxIssuesToLog:<max number of issues(errors and warnings) displayed on the ADO console>",
                 Strings.HelpText_DisplayHelp_AdoConsoleMaxIssuesToLog,
                 HelpLevel.Verbose);

            hw.WriteOption(
                "/treatAbsentDirectoryAsExistentUnderOpaque[+|-]",
                Strings.HelpText_DisplayHelp_TreatAbsentDirectoryAsExistentUnderOpaque,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/explicitlyReportDirectoryProbes[+|-]",
                Strings.HelpText_DisplayHelp_ExplicitlyReportDirectoryProbes,
                HelpLevel.Verbose
                );

            hw.WriteOption(
                "/enableLinuxEBPFSandbox[+|-]",
                Strings.HelpText_DisplayHelp_EnableLinuxEBPFSandbox,
                HelpLevel.Verbose
                );

            hw.WriteOption(
                "/enableLinuxPTraceSandbox[+|-]",
                Strings.HelpText_DisplayHelp_EnableLinuxPTraceSandbox,
                HelpLevel.Verbose
                );

            hw.WriteOption(
                "/alwaysRemoteInjectDetoursFrom32BitProcess[+|-]",
                Strings.HelpText_DisplayHelp_AlwaysRemoteInjectDetoursFrom32BitProcess,
                HelpLevel.Verbose
                );

            hw.WriteOption(
                "/forceAddExecutionPermission[+|-]",
                Strings.HelpText_DisplayHelp_ForceAddExecutionPermission,
                HelpLevel.Verbose
                );

            hw.WriteOption(
                "/PipProperty:[PipId:[PropertyAndValue]]",
                Strings.HelpText_DisplayHelp_PipProperty,
                HelpLevel.Verbose
                );

            hw.WriteOption(
                "/schedulerSimulator[+|-]",
                Strings.HelpText_DisplayHelp_SchedulerSimulator);

            #endregion

            hw.WriteBanner(
                Strings.HelpText_DisplayHelp_DistributedBuildBanner,
                HelpLevel.Verbose);

            #region DistributedBuild

            hw.WriteOption(
                "/distributedBuildRole:<distributed build role>",
                Strings.HelpText_DisplayHelp_DistributedBuildRole,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/distributedBuildServicePort:<local TCP port>",
                Strings.HelpText_DisplayHelp_DistributedBuildServicePort,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/distributedBuildWorker:<IP address or host name>:<local TCP port>",
                Strings.HelpText_DisplayHelp_DistributedBuildWorker,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/distributedBuildOrchestratorLocation:<IP address or host name>:<local TCP port>",
                Strings.HelpText_DisplayHelp_DistributedBuildOrchestratorLocation,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/machineHostName:<host name>",
                Strings.HelpText_DisplayHelp_MachineHostName,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/enableWorkerSourceFileMaterialization[+|-]",
                Strings.HelpText_DisplayHelp_EnableWorkerSourceFileMaterialization,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/orchestratorCpuMultiplier:<double>",
                Strings.HelpText_DisplayHelp_OrchestratorCpuMultiplier,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/validateDistribution:[+|-]",
                Strings.HelpText_DisplayHelp_ValidateDistribution,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/immediateWorkerRelease:[+|-]",
                Strings.HelpText_DisplayHelp_ImmediateWorkerRelease,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/earlyWorkerRelease:[+|-]",
                Strings.HelpText_DisplayHelp_EarlyWorkerRelease,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/earlyWorkerReleaseMultiplier:<double>",
                Strings.HelpText_DisplayHelp_EarlyWorkerReleaseMultiplier,
                HelpLevel.Verbose);
            #endregion

            hw.WriteBanner(
                Strings.HelpText_DisplayHelp_ProcessRemotingBanner,
                HelpLevel.Verbose);

            #region Process Remoting

            hw.WriteOption(
                "/enableProcessRemoting[+|-]",
                Strings.HelpText_DisplayHelp_EnableProcessRemoting,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/processCanRunRemoteTags:<semi-colon separated tags>",
                Strings.HelpText_DisplayHelp_ProcessCanRunRemoteTags,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/processMustRunLocalTags:<semi-colon separated tags>",
                Strings.HelpText_DisplayHelp_ProcessMustRunLocalTags,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/remotingThresholdMultiplier:<double>",
                Strings.HelpText_DisplayHelp_RemotingThresholdMultiplier,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/numRemoteAgentLeases:<int>",
                Strings.HelpText_DisplayHelp_NumRemoteAgentLeases,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/remoteAgentWaitTimeSec:<double>",
                Strings.HelpText_DisplayHelp_RemoteAgentWaitTimeSec,
                HelpLevel.Verbose);

            #endregion


            hw.WriteBanner(Strings.HelpText_DisplayHelp_DiagBanner);

            #region Diagnostics
            hw.WriteOption(
                "/diagnostic:<area>",
                Strings.HelpText_DisplayHelp_Diagnostic);

            hw.WriteOption(
                "/experiment.<flag>[+|-]",
                GetExperimentalFlagHelp());

            hw.WriteOption(
                "/breakOnUnexpectedFileAccess[+|-]",
                Strings.HelpText_DisplayHelp_BreakOnUnexpectedFileAccess,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/TranslateDirectory:<fromPath>::<toPath>",
                Strings.HelpText_DisplayHelp_TranslateDirectory,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/unsafe_MonitorFileAccesses[+|-]",
                Strings.HelpText_DisplayHelp_Unsafe_MonitorFileAccesses);

            hw.WriteOption(
                "/unsafe_IgnoreReparsePoints[+|-]",
                Strings.HelpText_DisplayHelp_Unsafe_IgnoreReparsePoints,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/unsafe_IgnoreFullReparsePointResolving[+|-]",
                Strings.HelpText_DisplayHelp_Unsafe_IgnoreFullReparsePointResolving,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/unsafe_IgnoreDynamicWritesOnAbsentProbes[+|-|:IgnoreNothing|:IgnoreDirectoryProbes|:IgnoreFileProbes|:IgnoreAll]",
                Strings.HelpText_DisplayHelp_Unsafe_IgnoreDynamicWritesOnAbsentProbes,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/sandboxKind:<sandbox kind>",
                Strings.HelpText_DisplayHelp_SandboxKind,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/unsafe_DisableDetours[+|-]",
                Strings.HelpText_DisplayHelp_Unsafe_DisableDetours,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/logProcessDetouringStatus[+|-]",
                Strings.HelpText_DisplayHelp_LogProcessDetouringStatus,
                HelpLevel.Verbose);

            hw.WriteOption(
                 "/hardExitOnErrorInDetours[+|-]",
                 Strings.HelpText_DisplayHelp_HardExitOnErrorInDetours,
                 HelpLevel.Verbose);

            hw.WriteOption(
                "/checkDetoursMessageCount[+|-]",
                Strings.HelpText_DisplayHelp_CheckDetoursMessageCount,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/allowInternalDetoursErrorNotificationFile[+|-]",
                Strings.HelpText_DisplayHelp_AllowInternalDetoursErrorNotificationFile,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/unsafe_ExistingDirectoryProbesAsEnumerations[+|-]",
                Strings.HelpText_DisplayHelp_Unsafe_ExistingDirectoryProbesAsEnumerations,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/unsafe_OptimizedAstConversion[+|-]",
                Strings.HelpText_DisplayHelp_Unsafe_OptimizedAstConversion,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/unsafe_PreserveOutputs[+|-|:Reset]",
                Strings.HelpText_DisplayHelp_Unsafe_PreserveOutputs,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/unsafe_UnexpectedFileAccessesAreErrors[+|-]",
                Strings.HelpText_DisplayHelp_Unsafe_UnexpectedFileAccessesAreErrors,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/debug_LoadGraph:<fingerprint | path | name>",
                Strings.HelpText_DisplayHelp_Debug_LoadGraph,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/launchDebugger",
                Strings.HelpText_DisplayHelp_LaunchDebugger,
                HelpLevel.Verbose);
            #endregion

            hw.WriteBanner(Strings.HelpText_DisplayHelp_ScriptBanner);

            #region DScript
            hw.WriteOption(
                "/debugScript[+|-]",
                Strings.HelpText_DisplayHelp_DebugScript);
            hw.WriteOption(
                "/debuggerBreakOnExit[+|-]",
                Strings.HelpText_DisplayHelp_DebuggerBreakOnExit);
            hw.WriteOption(
                "/debuggerPort",
                Strings.HelpText_DisplayHelp_DebuggerPort);

            hw.WriteOption(
                "/unsafe_disableCycleDetection[+|-]",
                Strings.HelpText_DisplayHelp_Unsafe_DisableCycleDetection);

            hw.WriteOption(
                "/maxFrontEndConcurrency:<number of processes>",
                Strings.HelpText_DisplayHelp_MaxFrontEndConcurrency);

            hw.WriteOption(
                "/enableIncrementalFrontEnd:[+|-]",
                Strings.HelpText_DisplayHelp_EnableIncrementalFrontEnd);

            hw.WriteOption(
                "/maxTypeCheckingConcurrency[+|-]",
                Strings.HelpText_DisplayHelp_MaxTypeCheckingConcurrency);

            hw.WriteOption(
                "/allowMissingSpecs[+|-]",
                Strings.HelpText_DisplayHelp_AllowMissingSpecs);

            #endregion

            #region Component Governance Manifest
            hw.WriteBanner(Strings.HelpText_DisplayHelp_CgManifestBanner);

            hw.WriteOption("/generateCgManifestForNugets:<file>",
                Strings.HelpText_DisplayHelp_GenerateCgManifest);

            hw.WriteOption("/validateCgManifestForNugets:<file>",
                Strings.HelpText_DisplayHelp_ValidateCgManifest);

            #endregion

            hw.WriteBanner(Strings.HelpText_DisplayHelp_MsBuildBanner);

            #region MSBuild

            hw.WriteOption(
                "/msbuild.logVerbosity:<q[uiet] | m[inimal] | n[ormal] | d[etailed] | diag[nostic]>",
                Strings.HelpText_DisplayHelp_MsBuild_LogVerbosity);

            hw.WriteOption(
                "/msbuild.enableBinLogTracing[+|-]",
                Strings.HelpText_DisplayHelp_MsBuild_EnableBinLogTracing);

            hw.WriteOption(
                "/msbuild.enableEngineTracing[+|-]",
                Strings.HelpText_DisplayHelp_MsBuild_EnableEngineTracing);

            #endregion

            hw.WriteBanner(Strings.HelpText_DisplayHelp_MiscBanner);

            #region Misc
            hw.WriteOption(
                "/usePartialEvaluation[+|-]",
                Strings.HelpText_DisplayHelp_UsePartialEvaluation,
                HelpLevel.Verbose);

            hw.WriteOption(
                "@<file>",
                Strings.HelpText_DisplayHelp_ResponseFile);

            hw.WriteOption(
                "/help:[standard|verbose]",
                Strings.HelpText_DisplayHelp_Help);

            hw.WriteOption(
                "/interactive[+|-]",
                Strings.HelpText_DisplayHelp_Interactive,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/noLogo",
                Strings.HelpText_DisplayHelp_NoLogo);

            hw.WriteOption(
                "/relatedActivityId:<guid>",
                Strings.HelpText_DisplayHelp_RelatedActivityId,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/vs[+|-]",
                Strings.HelpText_DisplayHelp_VS);

            hw.WriteOption(
                "/solutionName:<string>",
                Strings.HelpText_DisplayHelp_SolutionName,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/vsOutputSrc[+|-]",
                Strings.HelpText_DisplayHelp_VsOutputSrc,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/inputChanges:<file>",
                Strings.HelpText_DisplayHelp_InputChanges);

            hw.WriteOption(
                "/telemetryTagPrefix:<string>",
                Strings.HelpText_DisplayHelp_TelemetryTagPrefix);

            hw.WriteOption(
                "/unsafe_DisableGraphPostValidation[+|-]",
                Strings.HelpText_DisplayHelp_Unsafe_DisableGraphPostValidation);

            hw.WriteOption(
                "/logPipStaticFingerprintTexts[+|-]",
                Strings.HelpText_DisplayHelp_LogPipStaticFingerprintTexts);

            hw.WriteOption(
                "/posixDeleteMode:[NoRun|RunFirst|RunLast]",
                Strings.HelpText_DisplayHelp_PosixDeleteMode);

            hw.WriteOption("/minimumDiskSpaceForPipsGb:<int representing disk space in Gigabyte>",
                Strings.HelpText_DisplayHelp_MinimumDiskSpaceForPipsGb);

            hw.WriteOption("/numRetryFailedPipsOnAnotherWorker:<int>",
               Strings.HelpText_DisplayHelp_NumRetryFailedPipsOnAnotherWorker);

            hw.WriteOption("/enablePlugins[+|-]",
               Strings.HelpText_DisplayHelp_EnablePlugins,
               HelpLevel.Verbose);

            hw.WriteOption("/pluginPaths:<path>",
               Strings.HelpText_DisplayHelp_PluginPaths,
               HelpLevel.Verbose);

            hw.WriteOption("/exitOnNewGraph",
                Strings.HelpText_DisplayHelp_ExitOnNewGraph,
                HelpLevel.Verbose);

            hw.WriteOption("/buildManifestVerifyFileContentOnHashComputation",
                Strings.HelpText_DisplayHelp_BuildManifestVerifyFileContentOnHashComputation,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/unsafe_AssumeCleanOutputs[+|-]",
                Strings.HelpText_DisplayHelp_Unsafe_AssumeCleanOutputs,
                HelpLevel.Verbose);

            hw.WriteOption(
                "/verifyJournalForEngineVolumes[+|-]",
                Strings.HelpText_DisplayHelp_VerifyJournalForEngineVolumes,
                HelpLevel.Verbose);

            #endregion

            if (helpLevel < HelpLevel.Verbose)
            {
                hw.WriteLine();
                hw.WriteLine(Strings.HelpText_DisplayHelp_ShowingStandardHelp);
            }
        }

        private static string GetExperimentalFlagHelp()
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Strings.HelpText_DisplayHelp_Experimental__0,
                string.Join(", ", GetExperimentalFlagOptions()));
        }

        private static IEnumerable<string> GetExperimentalFlagOptions()
        {
            // TODO: Dynamic
            return new string[]
                   {
                       "ForceReadOnlyForRequestedReadWrite",
                       "UseSubstTargetForCache",
                       "CheckUpToDatenessFilesOnDiskDuringCacheLookup",
                       "UseGraphPatching",
                       "ConstructAndSaveBindingFingerprint",
                       "UseSpecPublicFacadeAndAstWhenAvailable",
                   };
        }
    }
}
