// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Scheduler.Fingerprints
{
    /// <summary>
    /// Version for breaking changes in pip fingerprinting (or what is stored per fingerprint).
    /// These versions are used to salt the fingerprint, so versions can live side by side in the same database.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1008:EnumsShouldHaveZeroValue")]
    public enum PipFingerprintingVersion
    {
        // IMPORTANT: These identifiers must only always increase and never overlap with a prior value. They are used
        // when we have to rev the serialization format of the PipCacheDescriptor

        /// <summary>
        /// V2 scheme in which weak and strong content fingerprints are distinguished.
        /// Increment the value below when changes to Detours are made, so the cache is invalidated.
        /// </summary>
        /// <remarks>
        /// Add reason for bumping up the version. In this way, you get git conflict if two or more people are bumping up the version at the same time.
        /// 
        /// 46: Add ReparsePointInfo to the cache metadata.
        /// 47: Switch <see cref="BuildXL.Storage.ContentHashingUtilities.CreateSpecialValue(byte)"/> used for <see cref="WellKnownContentHashes.AbsentFile"/> to use first byte instead of last byte.
        /// 48: Belated bump for removing unsafe_allowMissingOutputs option (unsafe arguments are part of the path set)
        /// 49: Added IgnoreDynamicWritesOnAbsentProbes to IUnsafeSandboxConfiguration
        /// 51: 50 is already used in a patched build (20180914.8.4)
        /// 52: Detours detects file probes using CreateFileW/NtCreateFile/ZwCreateFile
        /// 53: Added UnsafeDoubleWriteErrorsAreWarnings option
        /// 54: Added AbsentPathProbeUnderOpaquesMode to Process and WeakFingerPrint
        /// 55: Changed FileMaterializationInfo/FileContentInfo bond serialization
        /// 56: Added NeedsToRunInContainer, ContainerIsolationLevel and DoubleWritePolicy
        /// 57: Fixed enumeration in StoreContentForProcessAndCreateCacheEntryAsync
        /// 58: Added RequiresAdmin field into the process pip.
        /// 59: Report all accesses under shared opaque fix
        /// </remarks>
        TwoPhaseV2 = 59,
    }
}