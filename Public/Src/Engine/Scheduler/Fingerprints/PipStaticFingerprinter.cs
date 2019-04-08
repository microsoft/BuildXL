// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Scheduler.Fingerprints
{
    /// <summary>
    /// Computes pip static fingerprints.
    /// </summary>
    public sealed class PipStaticFingerprinter : PipFingerprinter
    {
        private readonly Func<DirectoryArtifact, ContentFingerprint> m_sealDirectoryFingerprintLookup;

        private readonly Func<DirectoryArtifact, ContentFingerprint> m_directoryProducerFingerprintLookup;

        /// <summary>
        /// Flag for excluding semi-stable hash on fingerprinting seal directories.
        /// </summary>
        /// <remarks>
        /// This flag should only be used for testing purpose.
        /// </remarks>
        public bool ExcludeSemiStableHashOnFingerprintingSealDirectory { get; set; }

        /// <summary>
        /// Create an instance of <see cref="PipStaticFingerprinter"/>.
        /// </summary>
        public PipStaticFingerprinter(
            PathTable pathTable,
            Func<DirectoryArtifact, ContentFingerprint> sealDirectoryFingerprintLookup = null,
            Func<DirectoryArtifact, ContentFingerprint> directoryProducerFingerprintLookup = null,
            ExtraFingerprintSalts? extraFingerprintSalts = null,
            PathExpander pathExpander = null)
            : base(
                  pathTable,
                  contentHashLookup: null,
                  extraFingerprintSalts: extraFingerprintSalts,
                  pathExpander: pathExpander,
                  pipDataLookup: null)
        {
            m_sealDirectoryFingerprintLookup = sealDirectoryFingerprintLookup ?? new Func<DirectoryArtifact, ContentFingerprint>(d => ContentFingerprint.Zero);
            m_directoryProducerFingerprintLookup = directoryProducerFingerprintLookup ?? new Func<DirectoryArtifact, ContentFingerprint>(d => ContentFingerprint.Zero);
        }

        /// <inheritdoc />
        protected override void AddDirectoryDependency(ICollectionFingerprinter fingerprinter, DirectoryArtifact directoryArtifact)
        {
            Contract.Requires(fingerprinter != null);
            fingerprinter.Add(directoryArtifact.Path, m_sealDirectoryFingerprintLookup(directoryArtifact).Hash);
        }

        /// <inheritdoc />
        protected override void AddWeakFingerprint(IFingerprinter fingerprinter, SealDirectory sealDirectory)
        {
            Contract.Requires(fingerprinter != null);
            Contract.Requires(sealDirectory != null);

            base.AddWeakFingerprint(fingerprinter, sealDirectory);

            // For non-composite shared opaque directories, contents and composed directories are always empty, and therefore the static fingerprint 
            // is not strong enough, i.e. multiple shared opaques can share the same directory root. So in this case we need to add the fingerprint of the producer
            if (sealDirectory.Kind == SealDirectoryKind.SharedOpaque && !sealDirectory.IsComposite)
            {
                DirectoryArtifact directory = sealDirectory.Directory;
                fingerprinter.Add(directory, m_directoryProducerFingerprintLookup(directory).Hash);
            }

            if (!ExcludeSemiStableHashOnFingerprintingSealDirectory && !sealDirectory.Kind.IsDynamicKind())
            {
                // A statically sealed directory can exist as multiple different instances, e.g., one can have partially sealed directories with the same root and member set.
                // To distinguish those instances, we include the semi stable hash as part of the static fingerprint.
                fingerprinter.Add("SemiStableHash", sealDirectory.SemiStableHash);
            }
        }
    }
}
