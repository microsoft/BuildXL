// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// A shared opaque seal directory that is the result of composing existing shared opaque directories
    /// </summary>
    public sealed class CompositeSharedOpaqueSealDirectory : SealDirectory
    {
        private readonly string m_contentFilter;

        /// <inheritdoc/>
        public override IReadOnlyList<DirectoryArtifact> ComposedDirectories { get; }

        /// <inheritdoc/>
        public override bool IsComposite => true;

        /// <inheritdoc/>
        public override string ContentFilter => m_contentFilter;

        /// <nodoc/>
        public CompositeSharedOpaqueSealDirectory(
            AbsolutePath directoryRoot,
            IReadOnlyList<DirectoryArtifact> composedDirectories,
            PipProvenance provenance,
            ReadOnlyArray<StringId> tags,
            string contentFilter) 
                : base(
                    directoryRoot, 
                    CollectionUtilities.EmptySortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>(OrdinalFileArtifactComparer.Instance),
                    CollectionUtilities.EmptySortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer>(OrdinalDirectoryArtifactComparer.Instance),
                    SealDirectoryKind.SharedOpaque, 
                    provenance, 
                    tags, 
                    CollectionUtilities.EmptyArray<StringId>().ToReadOnlyArray())
        {
            Contract.Requires(composedDirectories != null);

            ComposedDirectories = composedDirectories;
            m_contentFilter = contentFilter;
        }

        internal static CompositeSharedOpaqueSealDirectory InternalDeserializeCompositeSharedOpaqueSealDirectory(PipReader reader)
        {
            DirectoryArtifact artifact = reader.ReadDirectoryArtifact();
            var directory = new CompositeSharedOpaqueSealDirectory(
                artifact.Path,
                reader.ReadArray(reader1 => reader1.ReadDirectoryArtifact()),
                reader.ReadPipProvenance(),
                reader.ReadReadOnlyArray(reader1 => reader1.ReadStringId()),
                reader.ReadNullableString());

            directory.SetDirectoryArtifact(artifact);
            Contract.Assume(directory.IsInitialized && directory.Directory == artifact);

            return directory;
        }

        /// <inheritdoc />
        internal override void InternalSerialize(PipWriter writer)
        {
            Contract.Assume(IsInitialized, "SealDirectory pip construction must be completed by calling SetPartialSealId");
            writer.Write((byte)SealDirectoryType.CompositeSharedOpaqueDirectory);
            writer.Write(Directory);
            writer.WriteReadOnlyList(ComposedDirectories, (w, v) => w.Write(v));
            writer.Write(Provenance);
            writer.Write(Tags, (w, v) => w.Write(v));
            writer.WriteNullableString(m_contentFilter);
        }
    }
}
