// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities;

namespace BuildXL.Ipc.ExternalApi.Commands
{
    /// <summary>
    /// Command corresponding to the <see cref="Client.GetSealedDirectoryContent"/> API operation.
    /// </summary>
    public sealed class GetSealedDirectoryContentCommand : Command<List<SealedDirectoryFile>>
    {
        /// <summary>
        /// Directory id
        /// </summary>
        public DirectoryArtifact Directory { get; }

        /// <summary>
        /// Directory path
        /// </summary>
        public string FullDirectoryPath { get; }

        /// <nodoc />
        public GetSealedDirectoryContentCommand(DirectoryArtifact directory, string fullDirectoryPath)
        {
            Contract.Requires(directory.IsValid);

            Directory = directory;
            FullDirectoryPath = fullDirectoryPath;
        }

        /// <inheritdoc />
        public override string RenderResult(List<SealedDirectoryFile> commandResult)
        {
            Contract.Requires(commandResult != null);

            using (var wrapper = Pools.GetStringBuilder())
            {
                var sb = wrapper.Instance;

                sb.Append(commandResult.Count);
                sb.AppendLine();
                foreach (var file in commandResult)
                {
                    sb.AppendLine(file.Render());
                }

                return sb.ToString();
            }
        }

        /// <inheritdoc />
        public override bool TryParseResult(string result, out List<SealedDirectoryFile> commandResult)
        {
            commandResult = new List<SealedDirectoryFile>();

            using (var reader = new StringReader(result))
            {
                var count = reader.ReadLine();
                if (!int.TryParse(count, out var numberOfFiles))
                {
                    return false;
                }

                for (int i = 0; i < numberOfFiles; i++)
                {
                    string val = reader.ReadLine();
                    if (!SealedDirectoryFile.TryParse(val, out var sealedDirectoryFile))
                    {
                        return false;
                    }

                    commandResult.Add(sealedDirectoryFile);
                }

                return true;
            }
        }

        internal override void InternalSerialize(BinaryWriter writer)
        {
            writer.Write(Directory.Path.RawValue);
            writer.Write(Directory.PartialSealId);
            writer.Write(Directory.IsSharedOpaque);
            writer.Write(FullDirectoryPath);
        }

        internal static Command InternalDeserialize(BinaryReader reader)
        {
            var directory = new DirectoryArtifact(
                new AbsolutePath(reader.ReadInt32()),
                reader.ReadUInt32(),
                reader.ReadBoolean());
            return new GetSealedDirectoryContentCommand(directory, reader.ReadString());
        }
    }
}
