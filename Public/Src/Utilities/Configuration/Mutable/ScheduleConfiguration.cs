// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;

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
            MaximumRamUtilizationPercentage = 85;
            MinimumTotalAvailableRamMb = 500;
            AllowCopySymlink = true;
            ForceSkipDependencies = ForceSkipDependenciesMode.Disabled;
            UseHistoricalRamUsageInfo = true;
            ForceUseEngineInfoFromCache = false;

            // It is well-known that CPU-bound tasks should be oversubscribed with 1.25 times the number of logical cores.
            MaxProcesses = Environment.ProcessorCount * 5 / 4;
            MaxIO = Math.Max(1, Environment.ProcessorCount / 4);
            MaxLightProcesses = 1000;

            // Based on the benchmarks, the cache lookup limit is 2 times the number of logical cores.
            MaxCacheLookup = Environment.ProcessorCount * 2;
            MaxMaterialize = Environment.ProcessorCount * 2;

            MaxChooseWorkerCpu = 5;
            MaxChooseWorkerCacheLookup = 1;

            CanonicalizeFilterOutputs = true;

            UnsafeDisableGraphPostValidation = false;

            ProcessRetries = 0;

            UnsafeLazySymlinkCreation = false;
            UnexpectedSymlinkAccessReportingMode = UnexpectedSymlinkAccessReportingMode.All;
            StoreOutputsToCache = true;

            // TODO: Fix me.
            EnableLazyWriteFileMaterialization = false;

            // TODO: Should this ever be enabled? Default to on outside of CloudBuild for now.
            WriteIpcOutput = true;

            InferNonExistenceBasedOnParentPathInRealFileSystem = true;

            OutputMaterializationExclusionRoots = new List<AbsolutePath>();

            IncrementalScheduling = false;
            GraphAgnosticIncrementalScheduling = true;
            ComputePipStaticFingerprints = false;
            LogPipStaticFingerprintTexts = false;

            CreateHandleWithSequentialScanOnHashingOutputFiles = false;
            OutputFileExtensionsForSequentialScanHandleOnHashing = new List<PathAtom>();

            TelemetryTagPrefix = null;

            SkipHashSourceFile = false;

            UnsafeDisableSharedOpaqueEmptyDirectoryScrubbing = false;

            EarlyWorkerReleaseMultiplier = 0.5;
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
            UnsafeLazySymlinkCreation = template.UnsafeLazySymlinkCreation;
            UnexpectedSymlinkAccessReportingMode = template.UnexpectedSymlinkAccessReportingMode;
            StoreOutputsToCache = template.StoreOutputsToCache;

            EnableLazyWriteFileMaterialization = template.EnableLazyWriteFileMaterialization;
            WriteIpcOutput = template.WriteIpcOutput;
            OutputMaterializationExclusionRoots = pathRemapper.Remap(template.OutputMaterializationExclusionRoots);

            IncrementalScheduling = template.IncrementalScheduling;
            GraphAgnosticIncrementalScheduling = template.GraphAgnosticIncrementalScheduling;
            ComputePipStaticFingerprints = template.ComputePipStaticFingerprints;
            LogPipStaticFingerprintTexts = template.LogPipStaticFingerprintTexts;

            CreateHandleWithSequentialScanOnHashingOutputFiles = template.CreateHandleWithSequentialScanOnHashingOutputFiles;
            OutputFileExtensionsForSequentialScanHandleOnHashing = 
                new List<PathAtom>(template.OutputFileExtensionsForSequentialScanHandleOnHashing.Select(pathRemapper.Remap));

            TelemetryTagPrefix = template.TelemetryTagPrefix;

            MasterCpuMultiplier = template.MasterCpuMultiplier;
            MasterCacheLookupMultiplier = template.MasterCacheLookupMultiplier;
            SkipHashSourceFile = template.SkipHashSourceFile;

            UnsafeDisableSharedOpaqueEmptyDirectoryScrubbing = template.UnsafeDisableSharedOpaqueEmptyDirectoryScrubbing;
            EarlyWorkerRelease = template.EarlyWorkerRelease;
            EarlyWorkerReleaseMultiplier = template.EarlyWorkerReleaseMultiplier;
        }

        /// <inheritdoc />
        public bool StopOnFirstError { get; set; }

        /// <inheritdoc />
        public int MaxProcesses { get; set; }

        /// <inheritdoc />
        public int MaxMaterialize { get; set; }

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
        public int MinimumTotalAvailableRamMb { get; set; }

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
        public string EnvironmentFingerprint { get; set; }

        /// <inheritdoc />
        public bool CanonicalizeFilterOutputs { get; set; }

        /// <inheritdoc />
        public bool ScheduleMetaPips { get; set; }

        /// <inheritdoc />
        public int ProcessRetries { get; set; }

        /// <inheritdoc />
        public bool UnsafeLazySymlinkCreation { get; set; }

        /// <inheritdoc />
        public bool EnableLazyWriteFileMaterialization { get; set; }

        /// <inheritdoc />
        public bool WriteIpcOutput { get; set; }

        /// <inheritdoc />
        public UnexpectedSymlinkAccessReportingMode UnexpectedSymlinkAccessReportingMode { get; set; }

        /// <inheritdoc />
        public bool StoreOutputsToCache { get; set; }

        /// <inheritdoc />
        public bool InferNonExistenceBasedOnParentPathInRealFileSystem { get; set; }

        /// <inheritdoc />
        public bool IncrementalScheduling { get; set; }

        /// <inheritdoc />
        public bool GraphAgnosticIncrementalScheduling { get; set; }

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
        public double? MasterCpuMultiplier { get; set; }

        /// <inheritdoc />
        public double? MasterCacheLookupMultiplier { get; set; }

        /// <inheritdoc />
        public bool SkipHashSourceFile { get; set; }

        /// <inheritdoc />
        public bool UnsafeDisableSharedOpaqueEmptyDirectoryScrubbing { get; set; }

        /// <inheritdoc />
        public bool UseHistoricalCpuUsageInfo { get; set; }

        /// <inheritdoc />
        public bool EarlyWorkerRelease { get; set; }

        /// <inheritdoc />
        public double EarlyWorkerReleaseMultiplier { get; set; }
    }
}
