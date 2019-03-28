// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
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
            Contract.Requires(fileId.IsValid);

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
            writer.Write(File.Path.RawValue);
            writer.Write(File.RewriteCount);
            writer.Write(FullFilePath);
        }

        internal static Command InternalDeserialize(BinaryReader reader)
        {
            var file = new FileArtifact(
                new AbsolutePath(reader.ReadInt32()),
                reader.ReadInt32());
            return new MaterializeFileCommand(file, reader.ReadString());
        }
    }
}
