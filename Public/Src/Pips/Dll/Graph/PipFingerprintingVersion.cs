// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Storage.Fingerprints;

namespace BuildXL.Pips.Graph
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
        /// 60: Save AbsolutePath in the StaticOutputHashes
        /// 62: FileContentInfo - change how length/existence is stored.
        /// 63: IncrementalTool - change reparsepoint probes and enumeration probes to read.
        /// 65: 64 is already used since 20190903; change in UnsafeOption serialization (partial preserve outputs)
        /// 66: Changed rendering of VSO hashes
        /// 67: Added SourceChangeAffectedContents
        /// 68: Added ChildProcessesToBreakawayFromSandbox
        /// 69: Added dynamic existing probe.
        /// 70: Removed duplicates from ObservedAccessedFileNames.
        /// 71: Rename fields in weak fingerprint.
        /// 72: Added PreserveoutputsTrustLevel
        /// 73: Added Trust statically declared accesses
        /// 74: Added IgnoreCreateProcessReport in IUnsafeSandboxConfiguration.
        /// 75: Changed the type of <see cref="Utilities.Configuration.IUnsafeSandboxConfiguration.IgnoreDynamicWritesOnAbsentProbes"/> 
        ///     from <c>bool</c> to <see cref="Utilities.Configuration.DynamicWriteOnAbsentProbePolicy"/>
        /// 76: Put extra salt's options in weakfingerprint instead of ExecutionAndFingerprintOptionsHash.
        /// 77: Change semantics related to tracking dependencies under untracked scopes.
        /// 78: Add session id and related session of the build.
        /// 79: Change the field name in unsafe option from "PreserveOutputInfo" to nameof(PreserveOutputsInfo)
        /// </remarks>
        TwoPhaseV2 = 79,
    }
}