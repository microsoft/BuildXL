// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Text;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// A Pip that writes a static string to a file.
    /// </summary>
    public sealed class WriteFile : Pip
    {
        /// <summary>
        /// Destination path.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public FileArtifact Destination { get; }

        /// <summary>
        /// Contents of the file to be created.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public PipData Contents { get; }

        /// <summary>
        /// The encoding of the file to be created.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public WriteFileEncoding Encoding { get; }

        /// <inheritdoc />
        public override ReadOnlyArray<StringId> Tags { get; }

        /// <inheritdoc />
        public override PipProvenance Provenance { get; }

        /// <summary>
        /// Constructs a WriteFile
        /// </summary>
        public WriteFile(
            FileArtifact destination,
            PipData contents,
            WriteFileEncoding encoding,
            ReadOnlyArray<StringId> tags,
            PipProvenance provenance)
        {
            Contract.Requires(destination.IsValid);
            Contract.Requires(contents.IsValid);
            Contract.Requires(tags.IsValid);
            Contract.Requires(provenance != null);

            Provenance = provenance;
            Tags = tags;
            Destination = destination;
            Contents = contents;
            Encoding = encoding;
        }

        /// <inheritdoc />
        public override PipType PipType => PipType.WriteFile;

        #region Serialziation
        internal static WriteFile InternalDeserialize(PipReader reader)
        {
            return new WriteFile(
                reader.ReadFileArtifact(),
                reader.ReadPipData(),
                (WriteFileEncoding)reader.ReadByte(),
                reader.ReadReadOnlyArray(reader1 => reader1.ReadStringId()),
                reader.ReadPipProvenance());
        }

        /// <inheritdoc />
        internal override void InternalSerialize(PipWriter writer)
        {
            writer.Write(Destination);
            writer.Write(Contents);
            writer.Write((byte)Encoding);
            writer.Write(Tags, (w, v) => w.Write(v));
            writer.Write(Provenance);
        }
        #endregion
    }

    /// <summary>
    /// The supported encodings for WriteFile pips
    /// </summary>
    public enum WriteFileEncoding : byte
    {
        /// <nodoc />
        Utf8 = 0,

        /// <nodoc />
        Ascii = 1,
    }
}
