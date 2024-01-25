// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Graph
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
            PathExpander pathExpander = null,
            PipFingerprintSaltLookup pipFingerprintSaltLookup = null)
            : base(
                  pathTable,
                  contentHashLookup: null,
                  extraFingerprintSalts: extraFingerprintSalts,
                  pathExpander: pathExpander,
                  pipDataLookup: null,
                  pipFingerprintSaltLookup: pipFingerprintSaltLookup)
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
                        hasher.Add("IndependentFingerprintWithConfigFingerprint", completeFingerprint.Hash);
                        hasher.AddOrderIndependentCollection(
                            "DirectoryDependencyFingerprints", 
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
                && sealDirectory.Kind == SealDirectoryKind.SharedOpaque)
            {
                if (!sealDirectory.IsComposite)
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
                else if (sealDirectory.ComposedDirectories != null)
                {
                    using (var hasher = CreateHashingHelper(true))
                    {
                        hasher.Add("IndependentFingerprint", completeFingerprint.Hash);
                        hasher.AddCollection<DirectoryArtifact, IReadOnlyList<DirectoryArtifact>>(
                            "ComposedDirectoryFingerprints",
                            sealDirectory.ComposedDirectories,
                            (fp, d) => fp.Add(d.Path, m_sealDirectoryFingerprintLookup(d).Hash));
                        hasher.Add("ContentFilter", 
                            sealDirectory.ContentFilter.HasValue ? $"{sealDirectory.ContentFilter.Value.Kind} {sealDirectory.ContentFilter.Value.Regex}" : "");
                        hasher.Add("ActionKind", sealDirectory.CompositionActionKind.ToString());
                        completeFingerprint = new ContentFingerprint(hasher.GenerateHash());
                        completeFingerprintText = FingerprintTextEnabled
                            ? completeFingerprintText + Environment.NewLine + hasher.FingerprintInputText
                            : completeFingerprintText;
                    }
                }
            }

            return completeFingerprint;
        }

        /// <summary>
        /// Adds extra fingerprint salts and process-specific fingerprint salts to the independent fingerprint.
        /// </summary>
        /// <remarks>
        /// Independent fingerprint may have been calculated in a different build that has different extra fingerprint salts, or has no
        /// process-specific fingerprint salts. This can be the result of constructing the graph from graph fragments. The fragments
        /// themselves may have been constructed by some pips from a different build, and that constructions may have different extra fingerprint salts
        /// from the current build.
        ///
        /// Consider the following scenario where a build session consists of 2 builds, one consisting pips for constructing pip graph fragments, and the other 
        /// stitches the fragments together and executes the pips in the fragments. In the first build session, the 1st build has a pip that constructs
        /// a pip P in a graph fragment G with a fingerprint salt A. The fragment is then included in the 2nd build that executes P.
        ///
        /// In the next build session, the first build keeps using the fingerprint salt A for constructing the graph fragment G. Thus, in that build,
        /// the construction of G has a cache hit. In the second build, one changes the fingerprint salt to B, and expects P to have a cache miss. In this build
        /// we will have a graph cache miss because we have a different fingerprint salt B. However, during the graph construction, we still serialize
        /// the graph fragments generated in the first build. If we do not include the fingerprint salt B when calculating the pip's static weak fingerprint (when stitching the fragments together),
        /// then P will have the same static weak fingerprint as the one in the first build session. This can result in a hit in the incremental scheduling
        /// state and make P get skipped during the execution phase, and thus underbuild.
        /// </remarks>
        private ContentFingerprint AddConfigFingerprintToIndependentFingerprint(Pip pip, ContentFingerprint independentFingerprint, out string fingerprintInputText)
        {
            fingerprintInputText = string.Empty;
            using (var hasher = CreateHashingHelper(true))
            {
                hasher.Add("IndependentFingerprint", independentFingerprint.Hash);
                hasher.AddNested(PipFingerprintField.ExecutionAndFingerprintOptions, fp => ExtraFingerprintSalts.AddFingerprint(fp, pip.BypassFingerprintSalt));

                if (pip is Process process)
                {
                    AddProcessSpecificFingerprintSalt(hasher, process);
                }

                fingerprintInputText = FingerprintTextEnabled
                    ? fingerprintInputText + Environment.NewLine + hasher.FingerprintInputText
                    : fingerprintInputText;

                return new ContentFingerprint(hasher.GenerateHash());
            }
        }

        /// <inheritdoc />
        public override ContentFingerprint ComputeWeakFingerprint(Pip pip, out string fingerprintInputText)
        {
            ContentFingerprint fingerprint;
            if (FingerprintTextEnabled)
            {
                // Force compute weak fingerprint if fingerprint text is requested.
                fingerprint = base.ComputeWeakFingerprint(pip, out fingerprintInputText);
            }
            else if (pip.IndependentStaticFingerprint.Length > 0)
            {
                fingerprint = new ContentFingerprint(pip.IndependentStaticFingerprint);

                // See the remarks in AddConfigFingerprintToIndependentFingerprint for why we need to add the config fingerprint
                // to the independent fingerprint.
                fingerprint = AddConfigFingerprintToIndependentFingerprint(pip, fingerprint, out fingerprintInputText);
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
