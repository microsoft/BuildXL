// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Native.Processes.Unix;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using OperatingSystemHelper = BuildXL.Utilities.Core.OperatingSystemHelper;
using StructUtilities = BuildXL.Cache.ContentStore.Interfaces.Utils.StructUtilities;

namespace BuildXL.Pips.Graph
{
    /// <summary>
    /// A wrapper that it wraps around all the different salts we use to calculate the WeakFingerprint.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct ExtraFingerprintSalts : IEquatable<ExtraFingerprintSalts>
    {
        private static readonly ExtraFingerprintSalts s_defaultValue = new(
            ignoreSetFileInformationByHandle: false,
            ignoreZwRenameFileInformation: false,
            ignoreZwOtherFileInformation: false,
            ignoreNonCreateFileReparsePoints: false,
            ignoreReparsePoints: true, // TODO: Change this value when the default value for ignoreReparsePoints changes.
            ignoreFullReparsePointResolving: true, // TODO: Change this value when the default value for ignoreFullReparsePointResolving changes.
            ignoreUntrackedPathsInFullReparsePointResolving: false,
            ignorePreloadedDlls: true, // TODO: Change this value when the default value for ignorePreloadedDlls changes.
            ignoreGetFinalPathNameByHandle: true,
            existingDirectoryProbesAsEnumerations: false,
            disableDetours: false,
            monitorNtCreateFile: true,
            monitorZwCreateOpenQueryFile: false, // TODO:  Change this value when the default value for monitorZwCreateOpenQueryFile changes.
            fingerprintVersion: PipFingerprintingVersion.TwoPhaseV2,
            fingerprintSalt: null,
            observationReclassificationRulesHash: null,
            searchPathToolsHash: null,
            monitorFileAccesses: true,
            maskUntrackedAccesses: true,
            normalizeReadTimestamps: true,
            pipWarningsPromotedToErrors: false,
            validateDistribution: false,
            explicitlyReportDirectoryProbes: false,
            ignoreDeviceIoControlGetReparsePoint: true,
            honorDirectoryCasingOnDisk: false,
            linuxOSName: OperatingSystemHelper.IsLinuxOS ? OperatingSystemHelperExtension.GetLinuxDistribution().Id : string.Empty,
            linuxFingerprintingVersion: LinuxFingerprintingVersion.Version);

        /// <summary>
        /// Returns a default value for this struct.
        /// </summary>
        /// <returns>A status that is an instance of the struct with default field values.</returns>
        public static ExtraFingerprintSalts Default()
        {
            return s_defaultValue;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="config"> Configuration.</param>
        /// <param name="fingerprintSalt">The extra, optional fingerprint salt.</param>
        /// <param name="searchPathToolsHash">The extra, optional salt of path fragments of tool locations for tools using search path enumeration.</param>
        /// <param name="observationReclassificationRulesHash">The extra, optional salt of the user-provided reclassification rules.</param>
        public ExtraFingerprintSalts(
            IConfiguration config,
            string fingerprintSalt,
            ContentHash? searchPathToolsHash,
            ContentHash? observationReclassificationRulesHash)
            : this(
                config.Sandbox.UnsafeSandboxConfiguration.IgnoreSetFileInformationByHandle,
                config.Sandbox.UnsafeSandboxConfiguration.IgnoreZwRenameFileInformation,
                config.Sandbox.UnsafeSandboxConfiguration.IgnoreZwOtherFileInformation,
                config.Sandbox.UnsafeSandboxConfiguration.IgnoreNonCreateFileReparsePoints,
                config.Sandbox.UnsafeSandboxConfiguration.IgnoreReparsePoints,
                !config.EnableFullReparsePointResolving(),
                config.Sandbox.UnsafeSandboxConfiguration.IgnoreUntrackedPathsInFullReparsePointResolving,
                config.Sandbox.UnsafeSandboxConfiguration.IgnorePreloadedDlls,
                config.Sandbox.UnsafeSandboxConfiguration.IgnoreGetFinalPathNameByHandle,
                config.Sandbox.UnsafeSandboxConfiguration.ExistingDirectoryProbesAsEnumerations,
                config.Sandbox.UnsafeSandboxConfiguration.DisableDetours(),
                config.Sandbox.UnsafeSandboxConfiguration.MonitorNtCreateFile,
                config.Sandbox.UnsafeSandboxConfiguration.MonitorZwCreateOpenQueryFile,
                config.Sandbox.UnsafeSandboxConfiguration.MonitorFileAccesses,
                config.Sandbox.MaskUntrackedAccesses,
                config.Sandbox.NormalizeReadTimestamps,
                config.Distribution.ValidateDistribution,
                ArePipWarningsPromotedToErrors(config.Logging),
                PipFingerprintingVersion.TwoPhaseV2,
                fingerprintSalt,
                searchPathToolsHash,
                observationReclassificationRulesHash,
                config.Sandbox.ExplicitlyReportDirectoryProbes,
                config.Sandbox.UnsafeSandboxConfiguration.IgnoreDeviceIoControlGetReparsePoint,
                config.Cache.HonorDirectoryCasingOnDisk,
                OperatingSystemHelper.IsLinuxOS ? OperatingSystemHelperExtension.GetLinuxDistribution().Id : string.Empty,
                LinuxFingerprintingVersion.Version
            )
        {
        }

        /// <summary>
        /// Helper to compute whether pip stderr/stdout warnings are promoted to errors
        /// </summary>
        public static bool ArePipWarningsPromotedToErrors(ILoggingConfiguration loggingConfiguration)
        {
#pragma warning disable 618
            return (loggingConfiguration.TreatWarningsAsErrors && (loggingConfiguration.WarningsNotAsErrors.Count == 0 || !loggingConfiguration.WarningsNotAsErrors.Contains((int)SharedLogEventId.PipProcessWarning)))
                || loggingConfiguration.WarningsAsErrors.Contains((int)SharedLogEventId.PipProcessWarning);
#pragma warning restore 618
        }

#pragma warning disable CS1572
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="ignoreSetFileInformationByHandle">Whether the /unsafe_IgnoreSetFileInformationByHandle was passed to BuildXL.</param>
        /// <param name="ignoreZwRenameFileInformation">Whether the /unsafe_IgnoreZwRenameFileInformation was passed to BuildXL.</param>
        /// <param name="ignoreZwOtherFileInformation">Whether the /unsafe_IgnoreZwOtherFileInformation was passed to BuildXL.</param>
        /// <param name="ignoreNonCreateFileReparsePoints">Whether symlinks are followed for any other than CreateFile APIs.</param>
        /// <param name="ignoreReparsePoints">Whether the /unsafe_IgnoreReparsePoints was passed to BuildXL.</param>
        /// <param name="ignoreFullReparsePointResolving">Whether the /unsafe_IgnoreFullReparsePointResolving was passed to BuildXL.</param>
        /// <param name="ignoreUntrackedPathsInFullReparsePointResolving">Whether the /unsafe_IgnoreUntrackedPathsInFullReparsePointResolving was passed to BuildXL.</param>
        /// <param name="ignorePreloadedDlls">Whether the /unsafe_IgnorePreloadedDlls was passed to BuildXL.</param>
        /// <param name="ignoreGetFinalPathNameByHandle">Whether the /unsafe_IgnoreGetFinalPathNameByHandle was passed to BuildXL.</param>
        /// <param name="existingDirectoryProbesAsEnumerations">Whether the /unsafe_ExistingDirectoryProbesAsEnumerations was passed to BuildXL.</param>
        /// <param name="disableDetours">Whether the /unsafe_DisableDetours was passed to BuildXL.</param>
        /// <param name="monitorNtCreateFile">Whether the NtCreateFile is detoured.</param>
        /// <param name="monitorZwCreateOpenQueryFile">Whether the ZwCreateOpenQueryFile is detoured.</param>
        /// <param name="monitorFileAccesses">Whether BuildXL monitors file accesses.</param>
        /// <param name="maskUntrackedAccesses">Whether /maskUntrackedAccesses is enabled.</param>
        /// <param name="normalizeReadTimestamps">Whether /normalizeReadTimestamps is enabled.</param>
        /// <param name="validateDistribution">Whether /validateDistribution is enabled.</param>
        /// <param name="pipWarningsPromotedToErrors">Whether pip warnings are promoted to errors via the command line configuration</param>
        /// <param name="fingerprintVersion">The fingerprint version.</param>
        /// <param name="fingerprintSalt">The extra, optional fingerprint salt.</param>
        /// <param name="searchPathToolsHash">The extra, optional salt of path fragments of tool locations for tools using search path enumeration.</param>
        /// <param name="observationReclassificationRulesHash">The extra, optional salt of the user-provided reclassification rules.</param>
        /// <param name="explicitlyReportDirectoryProbes">Whether /unsafe_explicitlyReportDirectoryProbes was passed to BuildXL.</param>
        /// <param name="ignoreDeviceIoControlGetReparsePoint">Whether /ignoreDeviceIoControlGetReparsePoint was passed to BuildXL.</param>
        /// <param name="honorDirectoryCasingOnDisk">Whether /honorDirectoryCasingOnDisk was passed to BuildXL.</param>
        /// <param name="linuxOSName">The linux os name (Ubuntu or Mariner), string.Empty if current environment is windows</param>
        /// <param name="usingEBPFSandbox">Whether the EBPF sandbox is being used (on Linux)</param>
        /// <param name="linuxFingerprintingVersion">Version for Linux-specific breaking changes in pip fingerprinting</param>
        public ExtraFingerprintSalts(
            bool ignoreSetFileInformationByHandle,
            bool ignoreZwRenameFileInformation,
            bool ignoreZwOtherFileInformation,
            bool ignoreNonCreateFileReparsePoints,
            bool ignoreReparsePoints,
            bool ignoreFullReparsePointResolving,
            bool ignoreUntrackedPathsInFullReparsePointResolving,
            bool ignorePreloadedDlls,
            bool ignoreGetFinalPathNameByHandle,
            bool existingDirectoryProbesAsEnumerations,
            bool disableDetours,
            bool monitorNtCreateFile,
            bool monitorZwCreateOpenQueryFile,
            bool monitorFileAccesses,
            bool maskUntrackedAccesses,
            bool normalizeReadTimestamps,
            bool validateDistribution,
            bool pipWarningsPromotedToErrors,
            PipFingerprintingVersion fingerprintVersion,
            string fingerprintSalt,
            ContentHash? searchPathToolsHash,
            ContentHash? observationReclassificationRulesHash,
            bool explicitlyReportDirectoryProbes,
            bool ignoreDeviceIoControlGetReparsePoint,
            bool honorDirectoryCasingOnDisk,
            string linuxOSName,
            LinuxFingerprintingVersion linuxFingerprintingVersion)
        {
            IgnoreSetFileInformationByHandle = ignoreSetFileInformationByHandle;
            IgnoreZwRenameFileInformation = ignoreZwRenameFileInformation;
            IgnoreZwOtherFileInformation = ignoreZwOtherFileInformation;
            IgnoreNonCreateFileReparsePoints = ignoreNonCreateFileReparsePoints;
            IgnoreReparsePoints = ignoreReparsePoints;
            IgnoreFullReparsePointResolving = ignoreFullReparsePointResolving;
            IgnoreUntrackedPathsInFullReparsePointResolving = ignoreUntrackedPathsInFullReparsePointResolving;
            IgnorePreloadedDlls = ignorePreloadedDlls;
            ExistingDirectoryProbesAsEnumerations = existingDirectoryProbesAsEnumerations;
            DisableDetours = disableDetours;
            MonitorNtCreateFile = monitorNtCreateFile;
            MonitorZwCreateOpenQueryFile = monitorZwCreateOpenQueryFile;
            MonitorFileAccesses = monitorFileAccesses;
            MaskUntrackedAccesses = maskUntrackedAccesses;
            NormalizeReadTimestamps = normalizeReadTimestamps;
            ValidateDistribution = validateDistribution;
            FingerprintVersion = fingerprintVersion;
            FingerprintSalt = fingerprintSalt + EngineEnvironmentSettings.DebugFingerprintSalt;
            SearchPathToolsHash = searchPathToolsHash;
            GlobalObservationReclassificationRulesHash = observationReclassificationRulesHash;
            IgnoreGetFinalPathNameByHandle = ignoreGetFinalPathNameByHandle;
            PipWarningsPromotedToErrors = pipWarningsPromotedToErrors;
            m_calculatedSaltsFingerprint = null;
            ExplicitlyReportDirectoryProbes = explicitlyReportDirectoryProbes;
            IgnoreDeviceIoControlGetReparsePoint = ignoreDeviceIoControlGetReparsePoint;
            HonorDirectoryCasingOnDisk = honorDirectoryCasingOnDisk;
            LinuxOSName = linuxOSName;
            LinuxFingerprintingVersion = linuxFingerprintingVersion;
        }
#pragma warning restore CS1572

        // In <see cref="PipContentFingerprintingVersion.SinglePhaseV1"/>, this fingerprint maps directly to
        // a <see cref="PipCacheDescriptor"/>. In <see cref="PipContentFingerprintingVersion.TwoPhaseV2"/>,
        // one would instead look up a collection of 'path sets' (additional inputs) from which to derive a strong fingerprint.

        /// <summary>
        /// Whether /unsafe_ignoreZwRenameFileInformation flag was passed to BuildXL.
        /// </summary>
        public bool IgnoreZwRenameFileInformation { get; }

        /// <summary>
        /// Whether /unsafe_ignoreZwOtherFileInformation flag was passed to BuildXL.
        /// </summary>
        public bool IgnoreZwOtherFileInformation { get; }

        /// <summary>
        /// Whether /unsafe_ignoreNonCreateFileReparsePoints flag was passed to BuildXL.
        /// </summary>
        public bool IgnoreNonCreateFileReparsePoints { get; }

        /// <summary>
        /// Whether /unsafe_ignoreSetFileInformationByHandle flag was passed to BuildXL.
        /// </summary>
        public bool IgnoreSetFileInformationByHandle { get; }

        /// <summary>
        /// Whether /unsafe_ignoreReparsePoints flag was passed to BuildXL.
        /// </summary>
        public bool IgnoreReparsePoints { get; }

        /// <summary>
        /// Whether /unsafe_ignoreFullReparsePointResolving flag was passed to BuildXL.
        /// </summary>
        public bool IgnoreFullReparsePointResolving { get; }

        /// <summary>
        /// Whether /unsafe_ignoreUntrackedPathsInFullReparsePointResolving flag was passed to BuildXL.
        /// </summary>
        public bool IgnoreUntrackedPathsInFullReparsePointResolving { get; }

        /// <summary>
        /// Whether /unsafe_ignorePreloadedDlls flag was passed to BuildXL.
        /// </summary>
        public bool IgnorePreloadedDlls { get; }

        /// <summary>
        /// Whether /unsafe_disableDetours flag was passed to BuildXL.
        /// </summary>
        public bool DisableDetours { get; }

        /// <summary>
        /// Whether the NtCreateFile function was detoured.
        /// </summary>
        public bool MonitorNtCreateFile { get; }

        /// <summary>
        /// Whether the ZwCreateOpenQueryFile function was detoured.
        /// </summary>
        public bool MonitorZwCreateOpenQueryFile { get; }

        /// <summary>
        /// Whether /unsafe_ignoreGetFinalPathNameByHandle was passed to BuildXL.
        /// </summary>
        public bool IgnoreGetFinalPathNameByHandle { get; }

        /// <summary>
        /// Whether the existing directory probe is treated as an enumeration.
        /// </summary>
        public bool ExistingDirectoryProbesAsEnumerations { get; }

        /// <summary>
        /// Whether BuildXL monitors file accesses.
        /// </summary>
        public bool MonitorFileAccesses { get; }

        /// <summary>
        /// Whether /maskUntrackedAccesses flag was passed to BuildXL.
        /// </summary>
        public bool MaskUntrackedAccesses { get; }

        /// <summary>
        /// Whether /normalizeReadTimestamps flag was enabled (enabled by default).
        /// </summary>
        public bool NormalizeReadTimestamps { get; }

        /// <summary>
        /// Whether /validateDistribution flag was enabled (disabled by default).
        /// </summary>
        public bool ValidateDistribution { get; }

        /// <summary>
        /// The fingerprint version to be used.
        /// </summary>
        public PipFingerprintingVersion FingerprintVersion { get; }

        /// <summary>
        /// The extra, optional fingerprint salt.
        /// </summary>
        public string FingerprintSalt { get; set; }

        /// <summary>
        /// The hash of all the configured search path tools
        /// </summary>
        public ContentHash? SearchPathToolsHash { get; }
        
        /// <summary>
        /// The hash of all the global observation reclassifications
        /// </summary>
        public ContentHash? GlobalObservationReclassificationRulesHash { get; }

        /// <summary>
        /// Whether warnings from process stderr or stdout are promoted to errors.
        /// </summary>
        /// <remarks>
        /// It is necessary to track this because if warnings are errors, the pip should not be cached. If the user
        /// switches the value to promote the warning to an error, the pip needs to rerun. This is for the same reason
        /// that we don't cache pips that are errors.</remarks>
        public bool PipWarningsPromotedToErrors { get; }

        /// <summary>
        /// Whether /unsafe_explicitlyReportDirectoryProbes flag was passed to BuildXL. (disabled by default)
        /// </summary>
        public bool ExplicitlyReportDirectoryProbes { get; set; }

        /// <summary>
        /// Whether /ignoreDeviceIoControlGetReparsePoint flag was passed to BuildXL. (disabled by default)
        /// </summary>
        public bool IgnoreDeviceIoControlGetReparsePoint { get; set; }

        /// <summary>
        /// Whether /honorDirectoryCasingOnDIsk flag was passed to BuildXL. (disabled by default)
        /// </summary>
        public bool HonorDirectoryCasingOnDisk { get; set; }

        /// <summary>
        /// Linux OS name (Ubuntu or Mariner)
        /// </summary>
        public string LinuxOSName { get; }

        /// <summary>
        /// Version for Linux specific breaking changes in pip fingerprinting 
        /// </summary>
        public LinuxFingerprintingVersion LinuxFingerprintingVersion { get; }

        /// <nodoc />
        public static bool operator ==(ExtraFingerprintSalts left, ExtraFingerprintSalts right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ExtraFingerprintSalts left, ExtraFingerprintSalts right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Compare two ExtraFingerprintSalts
        /// </summary>
        /// <param name="other">The fingerprint salts to compare with</param>
        /// <returns>True if ExtraFingerprintSalts are the same</returns>
        public bool Equals(ExtraFingerprintSalts other)
        {
            return other.IgnoreZwRenameFileInformation == IgnoreZwRenameFileInformation
                && other.IgnoreZwOtherFileInformation == IgnoreZwOtherFileInformation
                && other.IgnoreNonCreateFileReparsePoints == IgnoreNonCreateFileReparsePoints
                && other.IgnoreSetFileInformationByHandle == IgnoreSetFileInformationByHandle
                && other.IgnoreReparsePoints == IgnoreReparsePoints
                && other.IgnoreFullReparsePointResolving == IgnoreFullReparsePointResolving
                && other.IgnoreUntrackedPathsInFullReparsePointResolving == IgnoreUntrackedPathsInFullReparsePointResolving
                && other.IgnorePreloadedDlls == IgnorePreloadedDlls
                && other.DisableDetours == DisableDetours
                && other.MonitorNtCreateFile == MonitorNtCreateFile
                && other.MonitorZwCreateOpenQueryFile == MonitorZwCreateOpenQueryFile
                && other.IgnoreGetFinalPathNameByHandle == IgnoreGetFinalPathNameByHandle
                && other.ExistingDirectoryProbesAsEnumerations == ExistingDirectoryProbesAsEnumerations
                && other.MonitorFileAccesses == MonitorFileAccesses
                && other.MaskUntrackedAccesses == MaskUntrackedAccesses
                && other.NormalizeReadTimestamps == NormalizeReadTimestamps
                && other.FingerprintVersion.Equals(FingerprintVersion)
                && other.FingerprintSalt.Equals(FingerprintSalt)
                && (other.SearchPathToolsHash.HasValue == SearchPathToolsHash.HasValue)
                && SearchPathToolsHash.Value.Equals(SearchPathToolsHash.Value)
                && (other.GlobalObservationReclassificationRulesHash.HasValue == SearchPathToolsHash.HasValue)
                && GlobalObservationReclassificationRulesHash.Value.Equals(GlobalObservationReclassificationRulesHash.Value)
                && other.PipWarningsPromotedToErrors == PipWarningsPromotedToErrors
                && other.ValidateDistribution == ValidateDistribution
                && other.ExplicitlyReportDirectoryProbes.Equals(ExplicitlyReportDirectoryProbes)
                && other.IgnoreDeviceIoControlGetReparsePoint.Equals(IgnoreDeviceIoControlGetReparsePoint)
                && other.HonorDirectoryCasingOnDisk.Equals(HonorDirectoryCasingOnDisk)
                && string.Equals(LinuxOSName, other.LinuxOSName)
                && LinuxFingerprintingVersion == other.LinuxFingerprintingVersion;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = IgnoreZwRenameFileInformation.GetHashCode();
                hashCode = (hashCode * 397) ^ IgnoreZwOtherFileInformation.GetHashCode();
                hashCode = (hashCode * 397) ^ IgnoreNonCreateFileReparsePoints.GetHashCode();
                hashCode = (hashCode * 397) ^ IgnoreSetFileInformationByHandle.GetHashCode();
                hashCode = (hashCode * 397) ^ IgnoreReparsePoints.GetHashCode();
                hashCode = (hashCode * 397) ^ IgnorePreloadedDlls.GetHashCode();
                hashCode = (hashCode * 397) ^ DisableDetours.GetHashCode();
                hashCode = (hashCode * 397) ^ MonitorNtCreateFile.GetHashCode();
                hashCode = (hashCode * 397) ^ MonitorZwCreateOpenQueryFile.GetHashCode();
                hashCode = (hashCode * 397) ^ IgnoreGetFinalPathNameByHandle.GetHashCode();
                hashCode = (hashCode * 397) ^ ExistingDirectoryProbesAsEnumerations.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)FingerprintVersion;
                hashCode = (hashCode * 397) ^ (FingerprintSalt?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ SearchPathToolsHash.GetHashCode();
                hashCode = (hashCode * 397) ^ GlobalObservationReclassificationRulesHash.GetHashCode();
                hashCode = (hashCode * 397) ^ MonitorFileAccesses.GetHashCode();
                hashCode = (hashCode * 397) ^ MaskUntrackedAccesses.GetHashCode();
                hashCode = (hashCode * 397) ^ NormalizeReadTimestamps.GetHashCode();
                hashCode = (hashCode * 397) ^ PipWarningsPromotedToErrors.GetHashCode();
                hashCode = (hashCode * 397) ^ ValidateDistribution.GetHashCode();
                hashCode = (hashCode * 397) ^ IgnoreFullReparsePointResolving.GetHashCode();
                hashCode = (hashCode * 397) ^ IgnoreUntrackedPathsInFullReparsePointResolving.GetHashCode();
                hashCode = (hashCode * 397) ^ ExplicitlyReportDirectoryProbes.GetHashCode();
                hashCode = (hashCode * 397) ^ IgnoreDeviceIoControlGetReparsePoint.GetHashCode();
                hashCode = (hashCode * 397) ^ HonorDirectoryCasingOnDisk.GetHashCode();
                hashCode = (hashCode * 397) ^ (string.IsNullOrEmpty(LinuxOSName) ? 0 : LinuxOSName.GetHashCode());
                hashCode = (hashCode * 397) ^ (LinuxFingerprintingVersion.GetHashCode());

                return hashCode;
            }
        }

        private CalculatedFingerprintTuple m_calculatedSaltsFingerprint;

        /// <summary>
        /// Fingerprint
        /// </summary>
        public Fingerprint CalculatedSaltsFingerprint => CalculatedWeakFingerprintTuple.Fingerprint;

        /// <summary>
        /// Fingerprint text
        /// </summary>
        public string CalculatedSaltsFingerprintText => CalculatedWeakFingerprintTuple.FingerprintText;

        private CalculatedFingerprintTuple CalculatedWeakFingerprintTuple => m_calculatedSaltsFingerprint ?? (m_calculatedSaltsFingerprint = ComputeWeakFingerprint());

        /// <summary>
        /// Add fields to fingerprint
        /// </summary>
        public void AddFingerprint(IHashingHelper fingerprinter, bool bypassFingerprintSalt)
        {
            if (!bypassFingerprintSalt && !string.IsNullOrEmpty(FingerprintSalt))
            {
                fingerprinter.Add(nameof(FingerprintSalt), FingerprintSalt);
            }

            if (SearchPathToolsHash.HasValue)
            {
                fingerprinter.Add(nameof(SearchPathToolsHash), SearchPathToolsHash.Value);
            }

            if (GlobalObservationReclassificationRulesHash.HasValue)
            {
                fingerprinter.Add(nameof(GlobalObservationReclassificationRulesHash), GlobalObservationReclassificationRulesHash.Value);
            }

            if (!MaskUntrackedAccesses)
            {
                fingerprinter.Add(nameof(MaskUntrackedAccesses), -1);
            }

            if (!NormalizeReadTimestamps)
            {
                fingerprinter.Add(nameof(NormalizeReadTimestamps), -1);
            }

            if (PipWarningsPromotedToErrors)
            {
                fingerprinter.Add(nameof(PipWarningsPromotedToErrors), 1);
            }

            if (ValidateDistribution)
            {
                fingerprinter.Add(nameof(ValidateDistribution), 1);
            }

            if (ExplicitlyReportDirectoryProbes)
            {
                fingerprinter.Add(nameof(ExplicitlyReportDirectoryProbes), 1);
            }

            if (IgnoreUntrackedPathsInFullReparsePointResolving)
            {
                fingerprinter.Add(nameof(IgnoreUntrackedPathsInFullReparsePointResolving), 1);
            }

            // We will eventually remove this flag from the fingerprint, but for now we want to trigger a cache
            // miss only when DeviceIoControl is not ignored, so we can validate the feature is not a breaking change for
            // customers.
            if (!IgnoreDeviceIoControlGetReparsePoint)
            {
                fingerprinter.Add(nameof(IgnoreDeviceIoControlGetReparsePoint), 1);
            }

            if (HonorDirectoryCasingOnDisk)
            {
                fingerprinter.Add(nameof(HonorDirectoryCasingOnDisk), 1);
            }

            fingerprinter.Add(nameof(FingerprintVersion), (int)FingerprintVersion);

            if (!string.IsNullOrEmpty(LinuxOSName))
            {
                fingerprinter.Add(nameof(LinuxOSName), LinuxOSName);
            }

            if (OperatingSystemHelper.IsLinuxOS)
            {
                fingerprinter.Add(nameof(LinuxFingerprintingVersion), (int) LinuxFingerprintingVersion);
            }
        }

        private CalculatedFingerprintTuple ComputeWeakFingerprint()
        {
            using (var hasher = new CoreHashingHelper(true))
            {
                AddFingerprint(hasher, bypassFingerprintSalt: false);
                return new CalculatedFingerprintTuple(hasher.GenerateHash(), hasher.FingerprintInputText);
            }
        }

        /// <summary>
        /// Simple memento class to hold a computed fingerprint and a corresponding fingerprint text.
        /// </summary>
        private class CalculatedFingerprintTuple
        {
            /// <nodoc/>
            internal Fingerprint Fingerprint { get; }

            /// <nodoc/>
            internal string FingerprintText { get; }

            /// <nodoc/>
            internal CalculatedFingerprintTuple(Fingerprint fingerprint, string fingerprintText)
            {
                Fingerprint = fingerprint;
                FingerprintText = fingerprintText;
            }
        }
    }
}
