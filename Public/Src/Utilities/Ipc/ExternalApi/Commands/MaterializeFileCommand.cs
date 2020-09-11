// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Utilities;

namespace BuildXL.Ipc.ExternalApi.Commands
{
    /// <summary>
    /// Command corresponding to the <see cref="Client.MaterializeFile"/> API operation.
    /// </summary>
    public sealed class MaterializeFileCommand : Command<bool>
    {
        /// <summary>File id.</summary>
        public FileArtifact File { get; }

        /// <summary>Fill file path.</summary>
        public string FullFilePath { get; }

        /// <nodoc />
        public MaterializeFileCommand(FileArtifact fileId, string fullFilePath)
        {
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
            writer.Write(File.IsValid);
            if (File.IsValid)
            {
                writer.Write(File.Path.RawValue);
                writer.Write(File.RewriteCount);
            }
            writer.Write(FullFilePath);
        }

        internal static Command InternalDeserialize(BinaryReader reader)
        {
            var file = reader.ReadBoolean()
                ? new FileArtifact(
                    new AbsolutePath(reader.ReadInt32()),
                    reader.ReadInt32())
                : FileArtifact.Invalid;
            return new MaterializeFileCommand(file, reader.ReadString());
        }
    }
}
