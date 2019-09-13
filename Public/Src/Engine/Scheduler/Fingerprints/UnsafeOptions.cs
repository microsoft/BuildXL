// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
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
        public static readonly ContentHash PreserveOutputsNotUsed = WellKnownContentHashes.AbsentFile;

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
        private readonly ContentHash? m_preserveOutputsSalt;

        /// <summary>
        /// Unsafe configuration.
        /// </summary>
        public readonly IUnsafeSandboxConfiguration UnsafeConfiguration;

        /// <summary>
        /// Preserve output salt.
        /// </summary>
        public ContentHash PreserveOutputsSalt => m_preserveOutputsSalt.HasValue ? m_preserveOutputsSalt.Value : PreserveOutputsNotUsed;

        /// <summary>
        /// Creates an instance of <see cref="UnsafeOptions"/>.
        /// </summary>
        /// <param name="unsafeConfiguration">The IUnsafeSandboxConfiguration for the pip</param>
        /// <param name="preserveOutputSalt">The preserveOutputsSalt to use when running the pip. NOTE: this should have
        /// the pip specific <see cref="BuildXL.Pips.Operations.Process.AllowPreserveOutputs"/> setting already applied.
        /// So if preserve outputs is disallwed for the pip, it should be set to <see cref="PreserveOutputsNotUsed"/></param>
        public UnsafeOptions(IUnsafeSandboxConfiguration unsafeConfiguration, ContentHash preserveOutputSalt)
            : this(unsafeConfiguration, preserveOutputSalt != PreserveOutputsNotUsed ? preserveOutputSalt : (ContentHash?)null)
        {
        }

        private UnsafeOptions(IUnsafeSandboxConfiguration unsafeConfiguration, ContentHash? preserveOutputSalt)
        {
            Contract.Requires(unsafeConfiguration != null);
            Contract.Requires((preserveOutputSalt.HasValue) == (preserveOutputSalt.HasValue && preserveOutputSalt.Value != PreserveOutputsNotUsed));

            UnsafeConfiguration = unsafeConfiguration;
            m_preserveOutputsSalt = unsafeConfiguration.PreserveOutputs == PreserveOutputsMode.Disabled ? null : preserveOutputSalt;
        }

        /// <summary>
        /// Checks if this instance of <see cref="UnsafeOptions"/> is as safe or safer than <paramref name="other"/>.
        /// </summary>
        public bool IsAsSafeOrSaferThan(UnsafeOptions other)
        {
            return UnsafeConfiguration.IsAsSafeOrSaferThan(other.UnsafeConfiguration) && IsPreserveOutputsSaltAsSafeOrSaferThan(other);
        }

        /// <summary>
        /// Checks if this instance of <see cref="UnsafeOptions"/> is less safe than <paramref name="other"/>.
        /// </summary>
        public bool IsLessSafeThan(UnsafeOptions other)
        {
            return !IsAsSafeOrSaferThan(other);
        }

        private bool IsPreserveOutputsSaltAsSafeOrSaferThan(UnsafeOptions otherUnsafeOptions)
        {
            return m_preserveOutputsSalt == null || 
                UnsafeConfiguration.PreserveOutputs == PreserveOutputsMode.Disabled ||
                (otherUnsafeOptions.UnsafeConfiguration.PreserveOutputs != PreserveOutputsMode.Disabled && m_preserveOutputsSalt.Value == otherUnsafeOptions.PreserveOutputsSalt);
        }

        /// <summary>
        /// Serializes this instance of <see cref="UnsafeOptions"/>.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            UnsafeConfiguration.Serialize(writer);
            writer.Write(m_preserveOutputsSalt, (w, o) => o.Serialize(w));
        }

        /// <summary>
        /// Deserializes into an instance of <see cref="UnsafeOptions"/>.
        /// </summary>
        public static UnsafeOptions Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var unsafeConfiguration = UnsafeSandboxConfigurationExtensions.Deserialize(reader);
            var preserveOutputsSalt = reader.ReadNullableStruct(r => new ContentHash(r));

            return new UnsafeOptions(unsafeConfiguration, preserveOutputsSalt);
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
    }
}
