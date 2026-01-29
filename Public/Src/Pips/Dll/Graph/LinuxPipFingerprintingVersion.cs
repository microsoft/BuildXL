// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Storage.Fingerprints;

namespace BuildXL.Pips.Graph
{
    /// <summary>
    /// Version for Linux specific breaking changes in pip fingerprinting.
    /// </summary>
    /// <remarks>
    /// This fingerprint is only used on Linux builds. In this way we can avoid invalidating e.g. Windows builds when the change is Linux-specific.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1008:EnumsShouldHaveZeroValue")]
    public enum LinuxFingerprintingVersion
    {
        // IMPORTANT: These identifiers must only always increase and never overlap with a prior value. They are used
        // when we have to rev the serialization format of the PipCacheDescriptor

        /// <summary>
        /// Version for Linux specific breaking changes in pip fingerprinting
        /// </summary>
        /// <remarks>
        /// Bump this version when there is an Linux specific breaking change in pip fingerprinting.
        /// </remarks>
        Version = 5
    }
}