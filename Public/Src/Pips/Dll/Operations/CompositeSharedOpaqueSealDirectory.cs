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
        private readonly SealDirectoryContentFilter? m_contentFilter;

        /// <inheritdoc/>
        public override IReadOnlyList<DirectoryArtifact> ComposedDirectories { get; }

        /// <inheritdoc/>
        public override SealDirectoryContentFilter? ContentFilter => m_contentFilter;

        /// <inheritdoc/>
        public override bool IsComposite => true;

        /// <nodoc/>
        public override SealDirectoryCompositionActionKind CompositionActionKind { get; }

        /// <nodoc/>
        public CompositeSharedOpaqueSealDirectory(
            AbsolutePath directoryRoot,
            IReadOnlyList<DirectoryArtifact> composedDirectories,
            PipProvenance provenance,
            ReadOnlyArray<StringId> tags,
            SealDirectoryContentFilter? contentFilter,
            SealDirectoryCompositionActionKind compositionAction) 
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
            Contract.Requires(compositionAction != SealDirectoryCompositionActionKind.None);

            Contract.Assert(compositionAction != SealDirectoryCompositionActionKind.NarrowDirectoryCone || (composedDirectories.Count == 1), "When action is NarrowDirectoryCone, exactly one directory must be provided.");

            ComposedDirectories = composedDirectories;
            CompositionActionKind = compositionAction;
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
                reader.ReadBoolean()
                    ? new SealDirectoryContentFilter((SealDirectoryContentFilter.ContentFilterKind)reader.ReadByte(), reader.ReadString())
                    : (SealDirectoryContentFilter?)null,
                (SealDirectoryCompositionActionKind)reader.ReadByte());

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
            if(m_contentFilter != null)
            {
                writer.Write(true);
                writer.Write((byte)m_contentFilter.Value.Kind);
                writer.Write(m_contentFilter.Value.Regex);
            }           
            else
            {
                writer.Write(false);
            }
            writer.Write((byte)CompositionActionKind);
        }
    }
}
