// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// A Pip that copies a single file.
    /// </summary>
    public sealed class CopyFile : Pip
    {
        /// <summary>
        /// Path to copy from.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Content)]
        public FileArtifact Source { get; }

        /// <summary>
        /// Path to copy to.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public FileArtifact Destination { get; }

        /// <inheritdoc />
        public override ReadOnlyArray<StringId> Tags { get; }

        /// <inheritdoc />
        public override PipProvenance Provenance { get; }

        /// <summary>
        /// Flag options controlling CopyFile pip behavior.
        /// This class is similar to Process.Options in order to keep conceptual integrity and
        /// to accommodate adding options for CopyFile in future development.
        /// </summary>
        public enum Options : byte
        {
            /// <summary>
            /// Default value of no options set
            /// </summary>
            None = 0,

            /// <summary>
            /// If set, the outputs of this copyFile must be left writable
            /// </summary>
            OutputsMustRemainWritable = 1 << 0,
        }

        /// <summary>
        /// Options for CopyFile (similar to Process).
        /// Inorder to accomodate later changes, m_options
        /// is stubbed out here so that as Options grows we can set them with m_options
        /// </summary>
        private readonly Options m_options;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="source">Path to copy from.</param>
        /// <param name="destination">Path to copy to.</param>
        /// <param name="tags">An optional array of tags to apply to the pip</param>
        /// <param name="provenance">Provenance of copy-file pip</param>
        /// <param name="options">Options for the copied file</param>
        public CopyFile(FileArtifact source, FileArtifact destination, ReadOnlyArray<StringId> tags, PipProvenance provenance, Options options = default(Options))
        {
            Contract.Requires(source.IsValid);
            Contract.Requires(destination.IsValid);
            Contract.Requires(tags.IsValid);
            Contract.Requires(provenance != null);

            Tags = tags;
            Provenance = provenance;
            Source = source;
            Destination = destination;
            m_options = options;
        }

        /// <inheritdoc />
        public override PipType PipType => PipType.CopyFile;

        /// <summary>
        /// Indicates if the outputs of this process must be left writable
        /// </summary>
        public bool OutputsMustRemainWritable => (m_options & Options.OutputsMustRemainWritable) != 0;

        #region Serialization

        /// <nodoc />
        internal static CopyFile InternalDeserialize(PipReader reader)
        {
            return new CopyFile(
                reader.ReadFileArtifact(),
                reader.ReadFileArtifact(),
                reader.ReadReadOnlyArray(reader1 => reader1.ReadStringId()),
                reader.ReadPipProvenance(),
                (Options)reader.ReadByte());
        }

        /// <nodoc />
        internal override void InternalSerialize(PipWriter writer)
        {
            writer.Write(Source);
            writer.Write(Destination);
            writer.Write(Tags, (w, v) => w.Write(v));
            writer.Write(Provenance);
            writer.Write((byte)m_options);
        }
        #endregion
    }
}
