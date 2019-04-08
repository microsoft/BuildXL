// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        /// <inheritdoc/>
        public override IReadOnlyList<DirectoryArtifact> ComposedDirectories { get; }

        /// <inheritdoc/>
        public override bool IsComposite => true;
        
        /// <nodoc/>
        public CompositeSharedOpaqueSealDirectory(
            AbsolutePath directoryRoot,
            IReadOnlyList<DirectoryArtifact> composedDirectories,
            PipProvenance provenance,
            ReadOnlyArray<StringId> tags) 
                : base(
                    directoryRoot, 
                    CollectionUtilities.EmptySortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>(OrdinalFileArtifactComparer.Instance), 
                    SealDirectoryKind.SharedOpaque, 
                    provenance, 
                    tags, 
                    CollectionUtilities.EmptyArray<StringId>().ToReadOnlyArray())
        {
            Contract.Requires(composedDirectories != null);

            ComposedDirectories = composedDirectories;
        }

        internal static CompositeSharedOpaqueSealDirectory InternalDeserializeCompositeSharedOpaqueSealDirectory(PipReader reader)
        {
            DirectoryArtifact artifact = reader.ReadDirectoryArtifact();
            var directory = new CompositeSharedOpaqueSealDirectory(
                artifact.Path,
                reader.ReadArray(reader1 => reader1.ReadDirectoryArtifact()),
                reader.ReadPipProvenance(),
                reader.ReadReadOnlyArray(reader1 => reader1.ReadStringId()));

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
        }
    }
}
