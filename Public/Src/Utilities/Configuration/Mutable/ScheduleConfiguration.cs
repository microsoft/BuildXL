// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Interop.Unix;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class ScheduleConfiguration : IScheduleConfiguration
    {
        /// <nodoc />
        public ScheduleConfiguration()
        {
            PinCachedOutputs = true;
            EnableLazyOutputMaterialization = true;
            UseHistoricalPerformanceInfo = true;
            TreatDirectoryAsAbsentFileOnHashingInputContent = true;
            MaximumRamUtilizationPercentage = 90;
            MaximumCommitUtilizationPercentage = 95;
            CriticalCommitUtilizationPercentage = 98;
            MaximumAllowedMemoryPressureLevel = Memory.PressureLevel.Normal;

            AllowCopySymlink = true;
            ForceSkipDependencies = ForceSkipDependenciesMode.Disabled;
            UseHistoricalRamUsageInfo = true;
            ForceUseEngineInfoFromCache = false;

            // In the past, we decided to use 1.25 * logicalCores to determine the concurrency for the process pips. 
            // However, at that time, cachelookup, materialization, and other non-execute steps were all taking a slot from that. 
            // As each major step runs in its own queue with a separate concurrency limit, we decided to revise using 1.25 multiplier.
            // After doing A/B testing on thousands of builds, using 1 instead of 1.25 multiplier decreases the load on the machine and improves the perf.
            MaxProcesses = Environment.ProcessorCount;
            MaxIO = Math.Max(1, Environment.ProcessorCount / 4);
            MaxLightProcesses = 1000;

            // Based on the benchmarks, the cache lookup limit is 2 times the number of logical cores.
            MaxCacheLookup = Environment.ProcessorCount * 2;
            MaxMaterialize = Environment.ProcessorCount * 2;
            MaxSealDirs = Environment.ProcessorCount;

            MaxChooseWorkerCpu = 5;
            MaxChooseWorkerCacheLookup = 1;

            CanonicalizeFilterOutputs = true;

            UnsafeDisableGraphPostValidation = false;
            UnsafeLazySODeletion = false;

            ProcessRetries = 0;

            StoreOutputsToCache = true;

            // TODO: Fix me.
            EnableLazyWriteFileMaterialization = false;

            // TODO: Should this ever be enabled? Default to on outside of CloudBuild for now.
            WriteIpcOutput = true;

            InferNonExistenceBasedOnParentPathInRealFileSystem = true;

            OutputMaterializationExclusionRoots = new List<AbsolutePath>();

            IncrementalScheduling = false;
            ComputePipStaticFingerprints = false;
            LogPipStaticFingerprintTexts = false;

            CreateHandleWithSequentialScanOnHashingOutputFiles = false;
            OutputFileExtensionsForSequentialScanHandleOnHashing = new List<PathAtom>();

            TelemetryTagPrefix = null;

            SkipHashSourceFile = false;

            UnsafeDisableSharedOpaqueEmptyDirectoryScrubbing = false;
            InputChanges = AbsolutePath.Invalid;
            UpdateFileContentTableByScanningChangeJournal = true;

            EnableSetupCostWhenChoosingWorker = false;
            EnableLessAggresiveMemoryProjection = false;
            MaxRetriesDueToRetryableFailures = 5;

            PluginLocations = new List<AbsolutePath>();
            TreatAbsentDirectoryAsExistentUnderOpaque = true;

            EnableProcessRemoting = false;
            ProcessCanRunRemoteTags = new List<string>();
            ProcessMustRunLocalTags = new List<string>();
            RemotingThresholdMultiplier = 1.5;

            // When choosing a worker for a module, we need to fill 2x capacity of the preferred worker before the next workers.
            // Running a module in another worker can be expensive, so that's why, we try to fill the double capacity for the first preferred worker.
            ModuleAffinityLoadFactor = 2;
        }

        /// <nodoc />
        public ScheduleConfiguration(IScheduleConfiguration template, PathRemapper pathRemapper)
        {
            Contract.Assume(template != null);

            MaxProcesses = template.MaxProcesses;
            MaxLightProcesses = template.MaxLightProcesses;
            MaxIO = template.MaxIO;
            MaxChooseWorkerCpu = template.MaxChooseWorkerCpu;
            MaxChooseWorkerCacheLookup = template.MaxChooseWorkerCacheLookup;

            MaxCacheLookup = template.MaxCacheLookup;
            MaxMaterialize = template.MaxMaterialize;
            EnvironmentFingerprint = template.EnvironmentFingerprint;

            DisableProcessRetryOnResourceExhaustion = template.DisableProcessRetryOnResourceExhaustion;
            StopOnFirstError = template.StopOnFirstError;
            LowPriority = template.LowPriority;
            EnableLazyOutputMaterialization = template.EnableLazyOutputMaterialization;
            ForceSkipDependencies = template.ForceSkipDependencies;
            UseHistoricalPerformanceInfo = template.UseHistoricalPerformanceInfo;
            RequiredOutputMaterialization = template.RequiredOutputMaterialization;
            TreatDirectoryAsAbsentFileOnHashingInputContent = template.TreatDirectoryAsAbsentFileOnHashingInputContent;
            MaximumRamUtilizationPercentage = template.MaximumRamUtilizationPercentage;
            MinimumTotalAvailableRamMb = template.MinimumTotalAvailableRamMb;
            MinimumDiskSpaceForPipsGb = template.MinimumDiskSpaceForPipsGb;
            MaximumAllowedMemoryPressureLevel = template.MaximumAllowedMemoryPressureLevel;
            AllowCopySymlink = template.AllowCopySymlink;
            AdaptiveIO = template.AdaptiveIO;
            ReuseOutputsOnDisk = template.ReuseOutputsOnDisk;
            UseHistoricalRamUsageInfo = template.UseHistoricalRamUsageInfo;
            VerifyCacheLookupPin = template.VerifyCacheLookupPin;
            PinCachedOutputs = template.PinCachedOutputs;
            CanonicalizeFilterOutputs = template.CanonicalizeFilterOutputs;
            ForceUseEngineInfoFromCache = template.ForceUseEngineInfoFromCache;

            UnsafeDisableGraphPostValidation = template.UnsafeDisableGraphPostValidation;

            ProcessRetries = template.ProcessRetries;
            StoreOutputsToCache = template.StoreOutputsToCache;

            EnableLazyWriteFileMaterialization = template.EnableLazyWriteFileMaterialization;
            WriteIpcOutput = template.WriteIpcOutput;
            OutputMaterializationExclusionRoots = pathRemapper.Remap(template.OutputMaterializationExclusionRoots);

            IncrementalScheduling = template.IncrementalScheduling;
            ComputePipStaticFingerprints = template.ComputePipStaticFingerprints;
            LogPipStaticFingerprintTexts = template.LogPipStaticFingerprintTexts;

            CreateHandleWithSequentialScanOnHashingOutputFiles = template.CreateHandleWithSequentialScanOnHashingOutputFiles;
            OutputFileExtensionsForSequentialScanHandleOnHashing =
                new List<PathAtom>(template.OutputFileExtensionsForSequentialScanHandleOnHashing.Select(pathRemapper.Remap));

            TelemetryTagPrefix = template.TelemetryTagPrefix;

            OrchestratorCpuMultiplier = template.OrchestratorCpuMultiplier;
            OrchestratorCacheLookupMultiplier = template.OrchestratorCacheLookupMultiplier;
            SkipHashSourceFile = template.SkipHashSourceFile;

            UnsafeDisableSharedOpaqueEmptyDirectoryScrubbing = template.UnsafeDisableSharedOpaqueEmptyDirectoryScrubbing;
            UnsafeLazySODeletion = template.UnsafeLazySODeletion;
            UseFixedApiServerMoniker = template.UseFixedApiServerMoniker;
            InputChanges = pathRemapper.Remap(template.InputChanges);
            UpdateFileContentTableByScanningChangeJournal = template.UpdateFileContentTableByScanningChangeJournal;
            CacheOnly = template.CacheOnly;
            EnableSetupCostWhenChoosingWorker = template.EnableSetupCostWhenChoosingWorker;
            MaxSealDirs = template.MaxSealDirs;
            EnableHistoricCommitMemoryProjection = template.EnableHistoricCommitMemoryProjection;
            MaximumCommitUtilizationPercentage = template.MaximumCommitUtilizationPercentage;
            CriticalCommitUtilizationPercentage = template.CriticalCommitUtilizationPercentage;
            DelayedCacheLookupMinMultiplier = template.DelayedCacheLookupMinMultiplier;
            DelayedCacheLookupMaxMultiplier = template.DelayedCacheLookupMaxMultiplier;
            MaxRetriesDueToLowMemory = template.MaxRetriesDueToLowMemory;
            MaxRetriesDueToRetryableFailures = template.MaxRetriesDueToRetryableFailures;
            EnableLessAggresiveMemoryProjection = template.EnableLessAggresiveMemoryProjection;
            ManageMemoryMode = template.ManageMemoryMode;
            EnablePlugin = template.EnablePlugin;
            PluginLocations = pathRemapper.Remap(template.PluginLocations);
            TreatAbsentDirectoryAsExistentUnderOpaque = template.TreatAbsentDirectoryAsExistentUnderOpaque;
            MaxWorkersPerModule = template.MaxWorkersPerModule;
            ModuleAffinityLoadFactor = template.ModuleAffinityLoadFactor;
            UseHistoricalCpuUsageInfo = template.UseHistoricalCpuUsageInfo;

            EnableProcessRemoting = template.EnableProcessRemoting;
            NumOfRemoteAgentLeases = template.NumOfRemoteAgentLeases;
            ProcessCanRunRemoteTags = new List<string>(template.ProcessCanRunRemoteTags);
            ProcessMustRunLocalTags = new List<string>(template.ProcessMustRunLocalTags);
            RemotingThresholdMultiplier = template.RemotingThresholdMultiplier;
            RemoteExecutionServiceUri = template.RemoteExecutionServiceUri;

            StopDirtyOnSucceedFastPips = template.StopDirtyOnSucceedFastPips;
            CpuResourceAware = template.CpuResourceAware;
        }

        /// <inheritdoc />
        public bool StopOnFirstError { get; set; }

        /// <inheritdoc />
        public int MaxProcesses { get; set; }

        /// <inheritdoc />
        public int MaxMaterialize { get; set; }

        /// <inheritdoc/>
        public bool StopDirtyOnSucceedFastPips { get; set; }

        /// <inheritdoc />
        public int MaxCacheLookup { get; set; }

        /// <inheritdoc />
        public int MaxIO { get; set; }

        /// <inheritdoc />
        public int MaxChooseWorkerCpu { get; set; }

        /// <inheritdoc />
        public int MaxChooseWorkerCacheLookup { get; set; }

        /// <inheritdoc />
        public bool LowPriority { get; set; }

        /// <inheritdoc />
        public bool AdaptiveIO { get; set; }

        /// <inheritdoc />
        public bool DisableProcessRetryOnResourceExhaustion { get; set; }

        /// <inheritdoc />
        public bool EnableLazyOutputMaterialization
        {
            get
            {
                return RequiredOutputMaterialization != RequiredOutputMaterialization.All;
            }

            set
            {
                // Only set this if not already equal to its boolean equivalent
                // (i.e., setting to true when existing is minimal
                // will not change the value)
                if (EnableLazyOutputMaterialization != value)
                {
                    RequiredOutputMaterialization = value ?
                        RequiredOutputMaterialization.Explicit :
                        RequiredOutputMaterialization.All;
                }
            }
        }

        /// <inheritdoc />
        public ForceSkipDependenciesMode ForceSkipDependencies { get; set; }

        /// <inheritdoc />
        public bool VerifyCacheLookupPin { get; set; }

        /// <inheritdoc />
        public bool PinCachedOutputs { get; set; }

        /// <inheritdoc />
        public bool UseHistoricalPerformanceInfo { get; set; }

        /// <inheritdoc />
        public bool ForceUseEngineInfoFromCache { get; set; }
        
        /// <inheritdoc />
        public bool? UseHistoricalRamUsageInfo { get; set; }

        /// <inheritdoc />
        public RequiredOutputMaterialization RequiredOutputMaterialization { get; set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<AbsolutePath> OutputMaterializationExclusionRoots { get; set; }

        /// <inheritdoc />
        IReadOnlyList<AbsolutePath> IScheduleConfiguration.OutputMaterializationExclusionRoots => OutputMaterializationExclusionRoots;

        /// <inheritdoc />
        public int MaxLightProcesses { get; set; }

        /// <inheritdoc />
        public bool TreatDirectoryAsAbsentFileOnHashingInputContent { get; set; }

        /// <inheritdoc />
        public bool AllowCopySymlink { get; set; }

        /// <inheritdoc />
        public int MaximumRamUtilizationPercentage { get; set; }

        /// <inheritdoc />
        public int? MinimumTotalAvailableRamMb { get; set; }

        /// <inheritdoc />
        public Memory.PressureLevel MaximumAllowedMemoryPressureLevel { get; set; }

        /// <nodoc />
        public int MinimumWorkers { get; set; }

        /// <summary>
        /// Checks up-to-dateness of files on disk during cache lookup using USN journal.
        /// </summary>
        public bool ReuseOutputsOnDisk { get; set; }

        /// <inheritdoc />
        /// <remarks>
        /// TODO: Remove this!
        /// </remarks>
        public bool UnsafeDisableGraphPostValidation { get; set; }

        /// <inheritdoc />
        public bool UnsafeLazySODeletion { get; set; }

        /// <inheritdoc />
        public string EnvironmentFingerprint { get; set; }

        /// <inheritdoc />
        public bool CanonicalizeFilterOutputs { get; set; }

        /// <inheritdoc />
        public bool ScheduleMetaPips { get; set; }

        /// <inheritdoc />
        public int ProcessRetries { get; set; }

        /// <inheritdoc />
        public bool EnableLazyWriteFileMaterialization { get; set; }

        /// <inheritdoc />
        public bool WriteIpcOutput { get; set; }

        /// <inheritdoc />
        public bool StoreOutputsToCache { get; set; }

        /// <inheritdoc />
        public bool InferNonExistenceBasedOnParentPathInRealFileSystem { get; set; }

        /// <inheritdoc />
        public bool IncrementalScheduling { get; set; }

        /// <inheritdoc />
        public bool ComputePipStaticFingerprints { get; set; }

        /// <inheritdoc />
        public bool LogPipStaticFingerprintTexts { get; set; }

        /// <inheritdoc />
        public bool CreateHandleWithSequentialScanOnHashingOutputFiles { get; set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<PathAtom> OutputFileExtensionsForSequentialScanHandleOnHashing { get; set; }

        /// <inheritdoc />
        IReadOnlyList<PathAtom> IScheduleConfiguration.OutputFileExtensionsForSequentialScanHandleOnHashing => OutputFileExtensionsForSequentialScanHandleOnHashing;

        /// <inheritdoc />
        public string TelemetryTagPrefix { get; set; }

        /// <inheritdoc />
        public double? OrchestratorCpuMultiplier { get; set; }

        /// <inheritdoc />
        public double? OrchestratorCacheLookupMultiplier { get; set; }

        /// <inheritdoc />
        public bool SkipHashSourceFile { get; set; }

        /// <inheritdoc />
        public bool UnsafeDisableSharedOpaqueEmptyDirectoryScrubbing { get; set; }

        /// <inheritdoc />
        public bool? UseHistoricalCpuUsageInfo { get; set; }

        /// <inheritdoc />
        public bool UseFixedApiServerMoniker { get; set; }

        /// <inheritdoc />
        public AbsolutePath InputChanges { get; set; }

        /// <inheritdoc />
        public int? MinimumDiskSpaceForPipsGb { get; set; }

        /// <inheritdoc />
        public int? MaxRetriesDueToLowMemory { get; set; }

        /// <inheritdoc />
        public int MaxRetriesDueToRetryableFailures { get; set; }

        /// <inheritdoc />
        public bool CacheOnly { get; set; }

        /// <inheritdoc />
        public bool EnableSetupCostWhenChoosingWorker { get; set;  }

        /// <inheritdoc />
        public int MaxSealDirs { get; set; }

        /// <inheritdoc />
        public bool EnableHistoricCommitMemoryProjection { get; set; }

        /// <inheritdoc />
        public int MaximumCommitUtilizationPercentage { get; set; }

        /// <inheritdoc />
        public int CriticalCommitUtilizationPercentage { get; set; }

        /// <inheritdoc />
        public double? DelayedCacheLookupMinMultiplier { get; set; }

        /// <inheritdoc />
        public double? DelayedCacheLookupMaxMultiplier { get; set; }

        /// <inheritdoc />
        public bool EnableLessAggresiveMemoryProjection { get; set; }

        /// <inheritdoc />
        public bool EnableEmptyingWorkingSet { get; set; }

        /// <inheritdoc />
        public ManageMemoryMode? ManageMemoryMode { get; set; }

        /// <inheritdoc />
        public bool? DisableCompositeOpaqueFilters { get; set; }

        /// <inheritdoc />
        public bool? EnablePlugin { get; set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<AbsolutePath> PluginLocations { get; set; }

        /// <inheritdoc />
        IReadOnlyList<AbsolutePath> IScheduleConfiguration.PluginLocations => PluginLocations;

        /// <inheritdoc />
        public bool TreatAbsentDirectoryAsExistentUnderOpaque { get; set; }

        /// <inheritdoc />
        public int MaxWorkersPerModule { get; set; }

        /// <inheritdoc />
        public double ModuleAffinityLoadFactor { get; set; }

        /// <inheritdoc />
        public bool UpdateFileContentTableByScanningChangeJournal { get; set; }

        /// <inheritdoc />
        public bool EnableProcessRemoting { get; set; }

        /// <inheritdoc />
        public int? NumOfRemoteAgentLeases { get; set; }

        /// <inheritdoc />
        public List<string> ProcessCanRunRemoteTags { get; set; }

        /// <inheritdoc />
        IReadOnlyList<string> IScheduleConfiguration.ProcessCanRunRemoteTags => ProcessCanRunRemoteTags;

        /// <inheritdoc />
        public List<string> ProcessMustRunLocalTags { get; set; }

        /// <inheritdoc />
        IReadOnlyList<string> IScheduleConfiguration.ProcessMustRunLocalTags => ProcessMustRunLocalTags;

        /// <inheritdoc />
        public int EffectiveMaxProcesses => MaxProcesses + (EnableProcessRemoting ? (NumOfRemoteAgentLeasesValue < 0 ? 0 : NumOfRemoteAgentLeasesValue) : 0);

        /// <inheritdoc />
        public double RemotingThresholdMultiplier { get; set; }

        private int NumOfRemoteAgentLeasesValue => NumOfRemoteAgentLeases ?? 2 * MaxProcesses;

        /// <inheritdoc />
        public string RemoteExecutionServiceUri { get; set; }

        /// <inheritdoc />
        public bool CpuResourceAware { get; set; }
    }
}
