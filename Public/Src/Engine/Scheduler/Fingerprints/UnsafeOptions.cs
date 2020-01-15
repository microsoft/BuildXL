// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;

namespace BuildXL.Scheduler.Fingerprints
{
    /// <summary>
    /// Unsafe options that become part of strong fingerprint.
    /// </summary>
    public class UnsafeOptions
    {
        /// <summary>
        /// Special marker used to denote safe preserve outputs salt.
        /// </summary>
        public static readonly PreserveOutputsInfo PreserveOutputsNotUsed = PreserveOutputsInfo.PreserveOutputsNotUsed;

        /// <summary>
        /// Safe values for the <see cref="UnsafeConfiguration"/> property.
        /// </summary>
        public static readonly IUnsafeSandboxConfiguration SafeConfigurationValues = UnsafeSandboxConfigurationExtensions.SafeDefaults;

        /// <summary>
        /// Safe options.
        /// </summary>
        public static readonly UnsafeOptions SafeValues = new UnsafeOptions(SafeConfigurationValues, PreserveOutputsNotUsed);

        /// <summary>
        /// Preserve output salt.
        /// </summary>
        /// <remarks>
        /// INVARIANT: this value is not <code>null</code> IFF it is different from <see cref="PreserveOutputsNotUsed"/>.
        /// </remarks>
        private readonly PreserveOutputsInfo? m_preservedOutputInfo;

        /// <summary>
        /// Unsafe configuration.
        /// </summary>
        public readonly IUnsafeSandboxConfiguration UnsafeConfiguration;

        /// <summary>
        /// Preserve output salt.
        /// </summary>
        public PreserveOutputsInfo PreserveOutputsSalt => m_preservedOutputInfo.HasValue ? m_preservedOutputInfo.Value : PreserveOutputsNotUsed;

        /// <summary>
        /// Creates an instance of <see cref="UnsafeOptions"/>.
        /// </summary>
        /// <param name="unsafeConfiguration">The IUnsafeSandboxConfiguration for the pip</param>
        /// <param name="preserveOutputInfo">The preserveOutputsSalt to use when running the pip. NOTE: this should have
        /// the pip specific <see cref="BuildXL.Pips.Operations.Process.AllowPreserveOutputs"/> setting already applied.
        /// So if preserve outputs is disallwed for the pip, it should be set to <see cref="PreserveOutputsNotUsed"/></param>
        public UnsafeOptions(IUnsafeSandboxConfiguration unsafeConfiguration, PreserveOutputsInfo preserveOutputInfo)
            : this(unsafeConfiguration, preserveOutputInfo != PreserveOutputsNotUsed ? preserveOutputInfo : (PreserveOutputsInfo?)null)
        {
        }

        private UnsafeOptions(IUnsafeSandboxConfiguration unsafeConfiguration, PreserveOutputsInfo? preserveOutputInfo)
        {
            Contract.Requires(unsafeConfiguration != null);
            Contract.Requires((preserveOutputInfo.HasValue) == (preserveOutputInfo.HasValue && preserveOutputInfo.Value != PreserveOutputsNotUsed));

            UnsafeConfiguration = unsafeConfiguration;
            m_preservedOutputInfo = unsafeConfiguration.PreserveOutputs == PreserveOutputsMode.Disabled ? null : preserveOutputInfo;
        }

        /// <summary>
        /// Checks if this instance of <see cref="UnsafeOptions"/> is as safe or safer than <paramref name="other"/>.
        /// </summary>
        public bool IsAsSafeOrSaferThan(UnsafeOptions other)
        {
            return UnsafeConfiguration.IsAsSafeOrSaferThan(other.UnsafeConfiguration) && IsPreserveOutputsAsSafeOrSaferThan(other);
        }

        /// <summary>
        /// Checks if this instance of <see cref="UnsafeOptions"/> is less safe than <paramref name="other"/>.
        /// </summary>
        public bool IsLessSafeThan(UnsafeOptions other)
        {
            return !IsAsSafeOrSaferThan(other);
        }

        private bool IsPreserveOutputsAsSafeOrSaferThan(UnsafeOptions otherUnsafeOptions)
        {
            return m_preservedOutputInfo == null || 
                UnsafeConfiguration.PreserveOutputs == PreserveOutputsMode.Disabled ||
                (otherUnsafeOptions.UnsafeConfiguration.PreserveOutputs != PreserveOutputsMode.Disabled && 
                    ( m_preservedOutputInfo.Value.IsAsSafeOrSaferThan(otherUnsafeOptions.PreserveOutputsSalt)));
        }

        /// <summary>
        /// Serializes this instance of <see cref="UnsafeOptions"/>.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            UnsafeConfiguration.Serialize(writer);
            writer.Write(m_preservedOutputInfo, (w, o) => o.Serialize(w));
        }

        /// <summary>
        /// Deserializes into an instance of <see cref="UnsafeOptions"/>.
        /// </summary>
        public static UnsafeOptions Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var unsafeConfiguration = UnsafeSandboxConfigurationExtensions.Deserialize(reader);
            var preserveOutputsInfo = reader.ReadNullableStruct(r => new PreserveOutputsInfo(r));

            return new UnsafeOptions(unsafeConfiguration, preserveOutputsInfo);
        }

        /// <summary>
        /// Calls <see cref="Deserialize(BuildXLReader)"/> catching any <see cref="System.IO.IOException"/>s.
        /// 
        /// If an exception is caught, <code>null</code> is returned.
        /// </summary>
        public static UnsafeOptions TryDeserialize([CanBeNull]BuildXLReader reader)
        {
            if (reader == null)
            {
                return null;
            }

            try
            {
                return Deserialize(reader);
            }
#pragma warning disable ERP022 // TODO: This should really handle specific errors
            catch
            {
                // Catch any exception during deserialization, e.g., malformed unsafe option.
                return null;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        /// <summary>
        /// Compute fingerprint associated with this unsafe options.
        /// </summary>
        public void ComputeFingerprint(IFingerprinter fingerprinter)
        {
            fingerprinter.Add(nameof(UnsafeConfiguration.SandboxKind), UnsafeConfiguration.SandboxKind.ToString());
            fingerprinter.Add(nameof(UnsafeConfiguration.ExistingDirectoryProbesAsEnumerations), getBoolString(UnsafeConfiguration.ExistingDirectoryProbesAsEnumerations));
            fingerprinter.Add(nameof(UnsafeConfiguration.IgnoreGetFinalPathNameByHandle), getBoolString(UnsafeConfiguration.IgnoreGetFinalPathNameByHandle));
            fingerprinter.Add(nameof(UnsafeConfiguration.IgnoreNonCreateFileReparsePoints), getBoolString(UnsafeConfiguration.IgnoreNonCreateFileReparsePoints));
            fingerprinter.Add(nameof(UnsafeConfiguration.IgnoreReparsePoints), getBoolString(UnsafeConfiguration.IgnoreReparsePoints));
            fingerprinter.Add(nameof(UnsafeConfiguration.IgnoreSetFileInformationByHandle), getBoolString(UnsafeConfiguration.IgnoreSetFileInformationByHandle));
            fingerprinter.Add(nameof(UnsafeConfiguration.IgnoreZwOtherFileInformation), getBoolString(UnsafeConfiguration.IgnoreZwOtherFileInformation));
            fingerprinter.Add(nameof(UnsafeConfiguration.IgnoreZwRenameFileInformation), getBoolString(UnsafeConfiguration.IgnoreZwRenameFileInformation));
            fingerprinter.Add(nameof(UnsafeConfiguration.MonitorFileAccesses), getBoolString(UnsafeConfiguration.MonitorFileAccesses));
            fingerprinter.Add(nameof(UnsafeConfiguration.MonitorNtCreateFile), getBoolString(UnsafeConfiguration.MonitorNtCreateFile));
            fingerprinter.Add(nameof(UnsafeConfiguration.MonitorZwCreateOpenQueryFile), getBoolString(UnsafeConfiguration.MonitorZwCreateOpenQueryFile));
            fingerprinter.Add(nameof(UnsafeConfiguration.PreserveOutputs), UnsafeConfiguration.PreserveOutputs.ToString());
            fingerprinter.Add(nameof(UnsafeConfiguration.UnexpectedFileAccessesAreErrors), getBoolString(UnsafeConfiguration.UnexpectedFileAccessesAreErrors));
            fingerprinter.Add(nameof(UnsafeConfiguration.IgnorePreloadedDlls), getBoolString(UnsafeConfiguration.IgnorePreloadedDlls));
            fingerprinter.Add(nameof(UnsafeConfiguration.IgnoreDynamicWritesOnAbsentProbes), UnsafeConfiguration.IgnoreDynamicWritesOnAbsentProbes.ToString());
            fingerprinter.Add(nameof(UnsafeConfiguration.DoubleWritePolicy), UnsafeConfiguration.DoubleWritePolicy.HasValue ? UnsafeConfiguration.DoubleWritePolicy.Value.ToString() : string.Empty);
            fingerprinter.Add(nameof(UnsafeConfiguration.IgnoreUndeclaredAccessesUnderSharedOpaques), getBoolString(UnsafeConfiguration.IgnoreUndeclaredAccessesUnderSharedOpaques));

            if (m_preservedOutputInfo.HasValue)
            {
                fingerprinter.AddNested("PreserveOutputInfo", fp => m_preservedOutputInfo.Value.ComputeFingerprint(fp));
            }

            static string getBoolString(bool value) => value ? "1" : "0";
        }
    }
}
