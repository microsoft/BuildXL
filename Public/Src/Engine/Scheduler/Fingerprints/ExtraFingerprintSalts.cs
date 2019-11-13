// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine.Cache;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler.Fingerprints
{
    /// <summary>
    /// A wrapper that it wraps around all the different salts we use to calculate the WeakFingerprint.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct ExtraFingerprintSalts : IEquatable<ExtraFingerprintSalts>
    {
        // For non-Unix platforms, this is an arbitrary fixed value
        private static readonly string s_requiredKextVersionNumber = OperatingSystemHelper.IsUnixOS 
            ? Processes.SandboxConnectionKext.RequiredKextVersionNumber
            : "0";

        private static readonly ExtraFingerprintSalts s_defaultValue = new ExtraFingerprintSalts(
                        ignoreSetFileInformationByHandle: false,
                        ignoreZwRenameFileInformation: false,
                        ignoreZwOtherFileInformation: false,
                        ignoreNonCreateFileReparsePoints: false,
                        ignoreReparsePoints: true, // TODO: Change this value when the default value for ignoreReparsePoints changes.
                        ignorePreloadedDlls: true, // TODO: Change this value when the default value for ignorePreloadedDlls changes.
                        ignoreGetFinalPathNameByHandle: true, // TODO: Change this value when the default value for ignoreGetFinalPathNameByHandle changes.
                        existingDirectoryProbesAsEnumerations: false,
                        disableDetours: false,
                        monitorNtCreateFile: true,
                        monitorZwCreateOpenQueryFile: false, // TODO:  Change this value when the default value for monitorZwCreateOpenQueryFile changes.
                        fingerprintVersion: PipFingerprintingVersion.TwoPhaseV2,
                        fingerprintSalt: null,
                        searchPathToolsHash: null,
                        monitorFileAccesses: true,
                        unexpectedFileAccessesAreErrors: true,
                        maskUntrackedAccesses: true,
                        normalizeReadTimestamps: true,
                        pipWarningsPromotedToErrors: false,
                        validateDistribution: false,
                        requiredKextVersionNumber: s_requiredKextVersionNumber
            );

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
        /// <param name="fingerprintVersion">The fingerprint version.</param>
        /// <param name="fingerprintSalt">The extra, optional fingerprint salt.</param>
        /// <param name="searchPathToolsHash">The extra, optional salt of path fragments of tool locations for tools using search path enumeration.</param>
        public ExtraFingerprintSalts(
            IConfiguration config,
            PipFingerprintingVersion fingerprintVersion,
            string fingerprintSalt,
            ContentHash? searchPathToolsHash)
            : this(
                config.Sandbox.UnsafeSandboxConfiguration.IgnoreSetFileInformationByHandle,
                config.Sandbox.UnsafeSandboxConfiguration.IgnoreZwRenameFileInformation,
                config.Sandbox.UnsafeSandboxConfiguration.IgnoreZwOtherFileInformation,
                config.Sandbox.UnsafeSandboxConfiguration.IgnoreNonCreateFileReparsePoints,
                config.Sandbox.UnsafeSandboxConfiguration.IgnoreReparsePoints,
                config.Sandbox.UnsafeSandboxConfiguration.IgnorePreloadedDlls,
                config.Sandbox.UnsafeSandboxConfiguration.IgnoreGetFinalPathNameByHandle,
                config.Sandbox.UnsafeSandboxConfiguration.ExistingDirectoryProbesAsEnumerations,
                config.Sandbox.UnsafeSandboxConfiguration.DisableDetours(),
                config.Sandbox.UnsafeSandboxConfiguration.MonitorNtCreateFile,
                config.Sandbox.UnsafeSandboxConfiguration.MonitorZwCreateOpenQueryFile,
                config.Sandbox.UnsafeSandboxConfiguration.MonitorFileAccesses,
                config.Sandbox.UnsafeSandboxConfiguration.UnexpectedFileAccessesAreErrors,
                config.Sandbox.MaskUntrackedAccesses,
                config.Sandbox.NormalizeReadTimestamps,
                config.Distribution.ValidateDistribution,
                ArePipWarningsPromotedToErrors(config.Logging),
                fingerprintVersion,
                fingerprintSalt,
                searchPathToolsHash,
                requiredKextVersionNumber: s_requiredKextVersionNumber
                )
        {
        }

        /// <summary>
        /// Helper to compute whether pip stderr/stdout warnings are promoted to errors
        /// </summary>
        public static bool ArePipWarningsPromotedToErrors(ILoggingConfiguration loggingConfiguration)
        {
            return (loggingConfiguration.TreatWarningsAsErrors && (loggingConfiguration.WarningsNotAsErrors.Count == 0 || !loggingConfiguration.WarningsNotAsErrors.Contains((int)EventId.PipProcessWarning)))
                || loggingConfiguration.WarningsAsErrors.Contains((int)EventId.PipProcessWarning);
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
        /// <param name="ignorePreloadedDlls">Whether the /unsafe_IgnorePreloadedDlls was passed to BuildXL.</param>
        /// <param name="ignoreGetFinalPathNameByHandle">Whether the /unsafe_IgnoreGetFinalPathNameByHandle was passed to BuildXL.</param>
        /// <param name="existingDirectoryProbesAsEnumerations">Whether the /unsafe_ExistingDirectoryProbesAsEnumerations was passed to BuildXL.</param>
        /// <param name="disableDetours">Whether the /unsafe_DisableDetours was passed to BuildXL.</param>
        /// <param name="monitorNtCreateFile">Whether the NtCreateFile is detoured.</param>
        /// <param name="monitorZwCreateOpenQueryFile">Whether the ZwCreateOpenQueryFile is detoured.</param>
        /// <param name="monitorFileAccesses">Whether BuildXL monitors file accesses.</param>
        /// <param name="unexpectedFileAccessesAreErrors">Whether /unsafe_unexpectedFileAccessesAreErrors was passed to BuildXL.</param>
        /// <param name="maskUntrackedAccesses">Whether /maskUntrackedAccesses is enabled.</param>
        /// <param name="normalizeReadTimestamps">Whether /normalizeReadTimestamps is enabled.</param>
        /// <param name="validateDistribution">Whether /validateDistribution is enabled.</param>
        /// <param name="pipWarningsPromotedToErrors">Whether pip warnings are promoted to errors via the command line configuration</param>
        /// <param name="fingerprintVersion">The fingerprint version.</param>
        /// <param name="fingerprintSalt">The extra, optional fingerprint salt.</param>
        /// <param name="searchPathToolsHash">The extra, optional salt of path fragments of tool locations for tools using search path enumeration.</param>
        /// <param name="requiredKextVersionNumber">The required kernel extension version number.</param>
        public ExtraFingerprintSalts(
            bool ignoreSetFileInformationByHandle,
            bool ignoreZwRenameFileInformation,
            bool ignoreZwOtherFileInformation,
            bool ignoreNonCreateFileReparsePoints,
            bool ignoreReparsePoints,
            bool ignorePreloadedDlls,
            bool ignoreGetFinalPathNameByHandle,
            bool existingDirectoryProbesAsEnumerations,
            bool disableDetours,
            bool monitorNtCreateFile,
            bool monitorZwCreateOpenQueryFile,
            bool monitorFileAccesses,
            bool unexpectedFileAccessesAreErrors,
            bool maskUntrackedAccesses,
            bool normalizeReadTimestamps,
            bool validateDistribution,
            bool pipWarningsPromotedToErrors,
            PipFingerprintingVersion fingerprintVersion,
            string fingerprintSalt,
            ContentHash? searchPathToolsHash,
            string requiredKextVersionNumber
            )
        {
            IgnoreSetFileInformationByHandle = ignoreSetFileInformationByHandle;
            IgnoreZwRenameFileInformation = ignoreZwRenameFileInformation;
            IgnoreZwOtherFileInformation = ignoreZwOtherFileInformation;
            IgnoreNonCreateFileReparsePoints = ignoreNonCreateFileReparsePoints;
            IgnoreReparsePoints = ignoreReparsePoints;
            IgnorePreloadedDlls = ignorePreloadedDlls;
            ExistingDirectoryProbesAsEnumerations = existingDirectoryProbesAsEnumerations;
            DisableDetours = disableDetours;
            MonitorNtCreateFile = monitorNtCreateFile;
            MonitorZwCreateOpenQueryFile = monitorZwCreateOpenQueryFile;
            MonitorFileAccesses = monitorFileAccesses;
            UnexpectedFileAccessesAreErrors = unexpectedFileAccessesAreErrors;
            MaskUntrackedAccesses = maskUntrackedAccesses;
            NormalizeReadTimestamps = normalizeReadTimestamps;
            ValidateDistribution = validateDistribution;
            FingerprintVersion = fingerprintVersion;
            FingerprintSalt = fingerprintSalt + EngineEnvironmentSettings.DebugFingerprintSalt;
            SearchPathToolsHash = searchPathToolsHash;
            IgnoreGetFinalPathNameByHandle = ignoreGetFinalPathNameByHandle;
            PipWarningsPromotedToErrors = pipWarningsPromotedToErrors;
            RequiredKextVersionNumber = requiredKextVersionNumber;
            m_calculatedSaltsFingerprint = null;
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
        /// Whether /unsafe_unexpectedFileAccessesAreErrors was passed to BuildXL
        /// </summary>
        public bool UnexpectedFileAccessesAreErrors { get; }

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
        /// Whether warnings from process stderr or stdout are promoted to errors.
        /// </summary>
        /// <remarks>
        /// It is necessary to track this because if warnings are errors, the pip should not be cached. If the user
        /// switches the value to promote the warning to an error, the pip needs to rerun. This is for the same reason
        /// that we don't cache pips that are errors.</remarks>
        public bool PipWarningsPromotedToErrors { get; }

        /// <summary>
        /// The required kernel extension version number. We want to make sure the fingerprints are locked to the
        /// sandbox kernel extension version and invalidate if it changes on subsequent builds.
        /// </summary>
        public string RequiredKextVersionNumber { get; set; }

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
                && other.IgnorePreloadedDlls == IgnorePreloadedDlls
                && other.DisableDetours == DisableDetours
                && other.MonitorNtCreateFile == MonitorNtCreateFile
                && other.MonitorZwCreateOpenQueryFile == MonitorZwCreateOpenQueryFile
                && other.IgnoreGetFinalPathNameByHandle == IgnoreGetFinalPathNameByHandle
                && other.ExistingDirectoryProbesAsEnumerations == ExistingDirectoryProbesAsEnumerations
                && other.MonitorFileAccesses == MonitorFileAccesses
                && other.UnexpectedFileAccessesAreErrors == UnexpectedFileAccessesAreErrors
                && other.MaskUntrackedAccesses == MaskUntrackedAccesses
                && other.NormalizeReadTimestamps == NormalizeReadTimestamps
                && other.FingerprintVersion.Equals(FingerprintVersion)
                && other.FingerprintSalt.Equals(FingerprintSalt)
                && (other.SearchPathToolsHash.HasValue == SearchPathToolsHash.HasValue)
                && SearchPathToolsHash.Value.Equals(SearchPathToolsHash.Value)
                && other.PipWarningsPromotedToErrors == PipWarningsPromotedToErrors
                && other.ValidateDistribution == ValidateDistribution
                && other.RequiredKextVersionNumber.Equals(RequiredKextVersionNumber);
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
                hashCode = (hashCode * 397) ^ MonitorFileAccesses.GetHashCode();
                hashCode = (hashCode * 397) ^ UnexpectedFileAccessesAreErrors.GetHashCode();
                hashCode = (hashCode * 397) ^ MaskUntrackedAccesses.GetHashCode();
                hashCode = (hashCode * 397) ^ NormalizeReadTimestamps.GetHashCode();
                hashCode = (hashCode * 397) ^ PipWarningsPromotedToErrors.GetHashCode();
                hashCode = (hashCode * 397) ^ ValidateDistribution.GetHashCode();
                hashCode = (hashCode * 397) ^ (RequiredKextVersionNumber?.GetHashCode() ?? 0);

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

        private CalculatedFingerprintTuple ComputeWeakFingerprint()
        {
            using (var hasher = new CoreHashingHelper(true))
            {
                if (!string.IsNullOrEmpty(FingerprintSalt))
                {
                    hasher.Add("FingerprintSalt", FingerprintSalt);
                }

                if (SearchPathToolsHash.HasValue)
                {
                    hasher.Add("SearchPathToolsHash", SearchPathToolsHash.Value);
                }

                if (!MaskUntrackedAccesses)
                {
                    hasher.Add("MaskUntrackedAccesses", -1);
                }

                if (!NormalizeReadTimestamps)
                {
                    hasher.Add("NormalizeReadTimestamps", -1);
                }

                if (PipWarningsPromotedToErrors)
                {
                    hasher.Add("PipWarningsPromotedToErrors", 1);
                }

                if (ValidateDistribution)
                {
                    hasher.Add("ValidateDistribution", 1);
                }

                if (!string.IsNullOrEmpty(RequiredKextVersionNumber))
                {
                    hasher.Add("RequiredKextVersionNumber", RequiredKextVersionNumber);
                }

                hasher.Add("Version", (int)FingerprintVersion);

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
