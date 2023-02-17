// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;
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
        /// Optional renderer to be used for WriteFile operation.
        /// </summary>
        public Options WriteFileOptions { get; }

        /// <summary>
        /// Constructs a WriteFile
        /// </summary>
        public WriteFile(
            FileArtifact destination,
            PipData contents,
            WriteFileEncoding encoding,
            ReadOnlyArray<StringId> tags,
            PipProvenance provenance,
            Options writeFileOptions = default)
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
            WriteFileOptions = writeFileOptions;
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
                reader.ReadPipProvenance(),
                reader.ReadWriteFileOptions());
        }

        /// <inheritdoc />
        internal override void InternalSerialize(PipWriter writer)
        {
            writer.Write(Destination);
            writer.Write(Contents);
            writer.Write((byte)Encoding);
            writer.Write(Tags, (w, v) => w.Write(v));
            writer.Write(Provenance);
            writer.Write(WriteFileOptions);
        }
        #endregion

        /// <summary>
        /// Flags for controlling WriteFile pip behaviour
        /// </summary>
        public struct Options
        {
            /// <summary>
            /// Specify how path separators should be rendered.
            /// </summary>
            public PathRenderingOption PathRenderingOption { get; }

            /// <summary>
            /// Constructor
            /// </summary>
            public Options(PathRenderingOption pathRenderingOption) => PathRenderingOption = pathRenderingOption;

            internal void Serialize(PipWriter writer)
            {
                Contract.Requires(writer != null);
                writer.Write((byte)PathRenderingOption);
            }

            internal static Options Deserialize(PipReader reader)
            {
                Contract.Requires(reader != null);
                return new Options((PathRenderingOption)reader.ReadByte());
            }

            /// <inheritdoc />
            public override string ToString() => $"Option={PathRenderingOption}";
        }

        /// <summary>
        /// Types of transformations that can be performed on a Path when it is rendered.
        /// </summary>
        /// <remarks>CODESYNC: Sync with matching type in  SdkRoot/Json/jsonSdk.dsc</remarks>
        public enum PathRenderingOption : byte
        {
            /// <summary>
            /// Render path as is
            /// </summary>
            None = 0,

            /// <summary>
            /// Always use back slashes as path separator
            /// </summary>
            BackSlashes = 1,

            /// <summary>
            /// Always use back slashes as path separator and add escape characters.
            /// </summary>
            EscapedBackSlashes = 2,

            /// <summary>
            /// Always use forward slashes as path separator
            /// </summary>
            ForwardSlashes = 3
        }
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
