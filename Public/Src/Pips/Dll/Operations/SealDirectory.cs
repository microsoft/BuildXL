// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// A <see cref="SealDirectory" /> pip is a scheduler-internal pip representing the completion of a directory.
    /// Once this pip's inputs are satisfied, the underlying directory (or the specified partial view) is immutable.
    /// </summary>
    public class SealDirectory : Pip
    {
        private uint? m_partialSealId;

        /// <summary>
        /// The kind of sealed directory
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public SealDirectoryKind Kind { get; }

        /// <summary>
        /// The patterns for source sealed directories
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public ReadOnlyArray<StringId> Patterns { get; }

        /// <summary>
        /// Root path that is sealed by this pip. Equivalent to <see cref="Directory"/>, except this is available before <see cref="IsInitialized"/> is set.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public AbsolutePath DirectoryRoot { get; }

        /// <summary>
        /// Upon completion, <see cref="Directory"/> contains these contents. If the seal is not Partial, then
        /// the directory contains exactly these contents.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> Contents { get; }

        /// <inheritdoc />
        public override ReadOnlyArray<StringId> Tags { get; }

        /// <inheritdoc />
        public override PipProvenance Provenance { get; }

        /// <summary>
        /// A seal directory can be composed of other seal directories. This is not the case for a regular seal directory,
        /// and therefore this collection is always empty. <see cref="CompositeSharedOpaqueSealDirectory"/>
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Content)]
        public virtual IReadOnlyList<DirectoryArtifact> ComposedDirectories => CollectionUtilities.EmptyArray<DirectoryArtifact>();

        /// <summary>
        /// Always false for a regular seal directory, <see cref="CompositeSharedOpaqueSealDirectory"/>
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public virtual bool IsComposite => false;

        /// <summary>
        /// Scrub the unsealed contents for fully seal directory. Always false when SealDirectoryKind is not Full.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public bool Scrub { get; }

        /// <summary>
        /// Creates a pip representing the hashing of the given source artifact.
        /// </summary>
        public SealDirectory(
            AbsolutePath directoryRoot,
            SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> contents,
            SealDirectoryKind kind,
            PipProvenance provenance,
            ReadOnlyArray<StringId> tags,
            ReadOnlyArray<StringId> patterns,
            bool scrub = false)
        {
            Contract.Requires(directoryRoot.IsValid);
            Contract.Requires(contents.IsValid);
            Contract.Requires(tags.IsValid);
            Contract.Requires(provenance != null);
            Contract.Requires(!(patterns.IsValid && patterns.Length != 0) || kind.IsSourceSeal(), "If patterns are provided, it must be a source seal directory");
            Contract.Requires(!scrub || kind.IsFull(), "Only scrub fully seal directory");

            Provenance = provenance;
            DirectoryRoot = directoryRoot;
            Contents = contents;
            Kind = kind;
            Tags = tags;
            Scrub = scrub;

            Patterns = patterns;

            // Initialization required before this pip is usable or serializable.
            m_partialSealId = null;
        }

        /// <inheritdoc />
        public override PipType PipType => PipType.SealDirectory;

        /// <summary>
        /// Indicates if this pip has completed initialization via <see cref="SetDirectoryArtifact"/>
        /// (expected during pip graph addition and before serialization).
        /// </summary>
        public bool IsInitialized => m_partialSealId.HasValue;

        /// <summary>
        /// The directory represented. This directory is immutable upon this pip's completion.
        /// </summary>
        public DirectoryArtifact Directory
        {
            get
            {
                Contract.Requires(IsInitialized);
                return new DirectoryArtifact(DirectoryRoot, m_partialSealId.Value, isSharedOpaque: Kind == SealDirectoryKind.SharedOpaque);
            }
        }

        /// <summary>
        /// Completes construction of this pip by assigning a unique directory artifact.
        /// </summary>
        internal void SetDirectoryArtifact(DirectoryArtifact artifact)
        {
            Contract.Requires(artifact.Path == DirectoryRoot);
            Contract.Requires(!IsInitialized || Directory.Equals(artifact));
            Contract.Ensures(IsInitialized);
            m_partialSealId = artifact.PartialSealId;
        }

        /// <summary>
        /// Resets the associated directory artifact (<see cref="m_partialSealId"/>).
        /// </summary>
        /// <remarks>
        /// This method should only be used for graph patching and in unit tests.
        /// </remarks>
        public void ResetDirectoryArtifact()
        {
            m_partialSealId = null;
        }

        /// <summary>
        /// Checks if the seal directory pip is a seal source directory.
        /// </summary>
        public bool IsSealSourceDirectory => Kind == SealDirectoryKind.SourceAllDirectories || Kind == SealDirectoryKind.SourceTopDirectoryOnly;

        #region Serialization
        internal static SealDirectory InternalDeserialize(PipReader reader)
        {
            var sealDirectoryType = (SealDirectoryType) reader.ReadByte();

            switch (sealDirectoryType)
            {
                case SealDirectoryType.SealDirectory:
                    return InternalDeserializeSealDirectory(reader);
                case SealDirectoryType.CompositeSharedOpaqueDirectory:
                    return CompositeSharedOpaqueSealDirectory.InternalDeserializeCompositeSharedOpaqueSealDirectory(reader);
                default:
                    throw new InvalidOperationException(I($"Unexpected seal directory type '{sealDirectoryType}'"));
            }
        }

        internal static SealDirectory InternalDeserializeSealDirectory(PipReader reader)
        {
            DirectoryArtifact artifact = reader.ReadDirectoryArtifact();
            var directory = new SealDirectory(
                artifact.Path,
                reader.ReadSortedReadOnlyArray(reader1 => reader1.ReadFileArtifact(), OrdinalFileArtifactComparer.Instance),
                (SealDirectoryKind)reader.ReadByte(),
                reader.ReadPipProvenance(),
                reader.ReadReadOnlyArray(reader1 => reader1.ReadStringId()),
                reader.ReadReadOnlyArray(reader1 => reader1.ReadStringId()),
                reader.ReadBoolean());

            directory.SetDirectoryArtifact(artifact);
            Contract.Assume(directory.IsInitialized && directory.Directory == artifact);

            return directory;
        }

        /// <inheritdoc />
        internal override void InternalSerialize(PipWriter writer)
        {
            Contract.Assume(IsInitialized, "SealDirectory pip construction must be completed by calling SetPartialSealId");
            writer.Write((byte)SealDirectoryType.SealDirectory);
            writer.Write(Directory);
            writer.Write(Contents, (w, v) => w.Write(v));
            writer.Write((byte)Kind);
            writer.Write(Provenance);
            writer.Write(Tags, (w, v) => w.Write(v));
            writer.Write(Patterns, (w, v) => w.Write(v));
            writer.Write(Scrub);
        }

        #endregion
    }
}
