// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

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
            m_sealDirectoryFingerprintLookup = sealDirectoryFingerprintLookup;
            m_directoryProducerFingerprintLookup = directoryProducerFingerprintLookup;
        }

        /// <summary>
        /// Completes pip's independent static weak fingerprint with the fingerprints of pip's dependencies when appropriate.
        /// </summary>
        private ContentFingerprint CompleteFingerprint(PipWithFingerprint pipWithFingerprint, out string completeFingerprintText)
        {
            ContentFingerprint completeFingerprint = pipWithFingerprint.Fingerprint;
            completeFingerprintText = pipWithFingerprint.FingerprintText ?? string.Empty;
            Pip pip = pipWithFingerprint.Pip;

            if (pip is Process process)
            {
                if (m_sealDirectoryFingerprintLookup != null)
                {
                    using (var hasher = CreateHashingHelper(true))
                    {
                        hasher.Add("IndependentFingerprint", completeFingerprint.Hash);
                        hasher.AddOrderIndependentCollection(
                            "DirectoryDependencies", 
                            process.DirectoryDependencies, 
                            (fp, d) => fp.Add(d.Path, m_sealDirectoryFingerprintLookup(d).Hash), 
                            DirectoryComparer);
                        completeFingerprint = new ContentFingerprint(hasher.GenerateHash());
                        completeFingerprintText = FingerprintTextEnabled 
                            ? completeFingerprintText + Environment.NewLine + hasher.FingerprintInputText 
                            : completeFingerprintText;
                    }
                }
            }
            else if (pip is SealDirectory sealDirectory 
                && sealDirectory.Kind == SealDirectoryKind.SharedOpaque 
                && !sealDirectory.IsComposite)
            {
                // For non-composite shared opaque directories, contents and composed directories are always empty, and therefore the static fingerprint 
                // is not strong enough, i.e. multiple shared opaques can share the same directory root. So in this case we need to add the fingerprint of the producer
                if (m_directoryProducerFingerprintLookup != null)
                {
                    DirectoryArtifact directory = sealDirectory.Directory;
                    using (var hasher = CreateHashingHelper(true))
                    {
                        hasher.Add("IndependentFingerprint", completeFingerprint.Hash);
                        hasher.Add(directory, m_directoryProducerFingerprintLookup(directory).Hash);
                        completeFingerprint = new ContentFingerprint(hasher.GenerateHash());
                        completeFingerprintText = FingerprintTextEnabled
                            ? completeFingerprintText + Environment.NewLine + hasher.FingerprintInputText
                            : completeFingerprintText;
                    }
                }
            }

            return completeFingerprint;
        }

        /// <inheritdoc />
        public override ContentFingerprint ComputeWeakFingerprint(Pip pip, out string fingerprintInputText)
        {
            ContentFingerprint fingerprint;
            fingerprintInputText = string.Empty;

            if (FingerprintTextEnabled)
            {
                // Force compute weak fingerprint if fingerprint text is requested.
                fingerprint = base.ComputeWeakFingerprint(pip, out fingerprintInputText);
            }
            else if (pip.IndependentStaticFingerprint.Length > 0)
            {
                fingerprint = new ContentFingerprint(pip.IndependentStaticFingerprint);
            }
            else
            {
                fingerprint = base.ComputeWeakFingerprint(pip, out fingerprintInputText);
            }

            return CompleteFingerprint(new PipWithFingerprint(pip, fingerprint, fingerprintInputText), out fingerprintInputText);
        }

        /// <inheritdoc />
        protected override void AddWeakFingerprint(IFingerprinter fingerprinter, SealDirectory sealDirectory)
        {
            Contract.Requires(fingerprinter != null);
            Contract.Requires(sealDirectory != null);

            base.AddWeakFingerprint(fingerprinter, sealDirectory);

            if (!ExcludeSemiStableHashOnFingerprintingSealDirectory && !sealDirectory.Kind.IsDynamicKind())
            {
                // A statically sealed directory can exist as multiple different instances, e.g., one can have partially sealed directories with the same root and member set.
                // To distinguish those instances, we include the semi stable hash as part of the static fingerprint.
                fingerprinter.Add("SemiStableHash", sealDirectory.SemiStableHash);
            }
        }

        /// <summary>
        /// Pip with its fingerprint.
        /// </summary>
        private struct PipWithFingerprint
        {
            /// <summary>
            /// Pip.
            /// </summary>
            public readonly Pip Pip;

            /// <summary>
            /// (Static) Fingerprint.
            /// </summary>
            public readonly ContentFingerprint Fingerprint;

            /// <summary>
            /// Fingerprint text.
            /// </summary>
            public readonly string FingerprintText;

            /// <summary>
            /// Creates an instance of <see cref="PipWithFingerprint"/>.
            /// </summary>
            public PipWithFingerprint(Pip pip, ContentFingerprint fingerprint, string fingerprintText)
            {
                Pip = pip;
                Fingerprint = fingerprint;
                FingerprintText = fingerprintText;
            }
        }
    }
}
