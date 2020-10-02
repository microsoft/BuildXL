// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities;

namespace BuildXL.Ipc.ExternalApi.Commands
{
    /// <summary>
    /// Command corresponding to the <see cref="Client.RegisterFileForBuildManifest"/> API operation.
    /// </summary>
    public sealed class RegisterFileForBuildManifestCommand : Command<bool>
    {
        /// <summary>
        /// DropName to identify which drop <see cref="RelativePath"/> belongs to.
        /// </summary>
        public string DropName { get; }

        /// <summary>
        /// Relative File Path of given file inside the drop.
        /// </summary>
        public string RelativePath { get; }

        /// <summary>
        /// Default Content Hash of the file. Generally VSO hash.
        /// </summary>
        public ContentHash Hash { get; }

        /// <summary>File id. Used if file needs to be materialized.</summary>
        public FileArtifact File { get; }

        /// <summary>Full file path on disk. Used if file needs to be materialized.</summary>
        public string FullFilePath { get; }

        /// <nodoc />
        public RegisterFileForBuildManifestCommand(
            string dropName,
            string path,
            ContentHash hash,
            FileArtifact fileId,
            string fullFilePath)
        {
            DropName = dropName;
            RelativePath = path;
            Hash = hash;
            File = fileId;
            FullFilePath = fullFilePath;
        }

        /// <inheritdoc />
        public override bool TryParseResult(string result, out bool commandResult)
        {
            commandResult = false;
            return bool.TryParse(result, out commandResult);
        }

        /// <inheritdoc />
        public override string RenderResult(bool commandResult)
        {
            return commandResult.ToString();
        }

        internal override void InternalSerialize(BinaryWriter writer)
        {
            writer.Write(DropName);
            writer.Write(RelativePath);
            Hash.Serialize(writer);
            writer.Write(File.Path.RawValue);
            writer.Write(File.RewriteCount);
            writer.Write(FullFilePath);
        }

        internal static Command InternalDeserialize(BinaryReader reader)
        {
            string dropName = reader.ReadString();
            string path = reader.ReadString();
            ContentHash hash = new ContentHash(reader);
            var file = new FileArtifact(
                new AbsolutePath(reader.ReadInt32()),
                reader.ReadInt32());
            string fullFilePath = reader.ReadString();

            return new RegisterFileForBuildManifestCommand(dropName, path, hash, file, fullFilePath);
        }
    }
}
