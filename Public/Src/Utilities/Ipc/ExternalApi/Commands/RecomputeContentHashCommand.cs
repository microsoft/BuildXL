// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Utilities.Core;

namespace BuildXL.Ipc.ExternalApi.Commands
{
    /// <summary>
    /// Command corresponding to the <see cref="Client.RecomputeContentHashFiles"/> API operation.
    /// client send a request with fileArtifac, request hashType and RecomputeContentHashEntry <see cref="RecomputeContentHashEntry"/>
    /// and expects a server to return response of content hash with given hash type
    /// </summary>
    public sealed class RecomputeContentHashCommand : Command<RecomputeContentHashEntry>
    {
        /// <summary>
        /// File artifact
        /// </summary>
        public FileArtifact File { get; }

        /// <summary>
        /// Request hash Type
        /// </summary>
        public string RequestedHashType { get; }

        /// <summary>
        /// A data structure represents the content hash computation entry <see cref="RecomputeContentHashEntry"/>
        /// </summary>
        public RecomputeContentHashEntry Entry { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        public RecomputeContentHashCommand(FileArtifact file, string hashType, RecomputeContentHashEntry entry)
        {
            File = file;
            RequestedHashType = hashType;
            Entry = entry;
        }

        /// <inheritdoc />
        public override string RenderResult(RecomputeContentHashEntry commandResult)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                commandResult.Serialize(writer);
                writer.Flush();
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        /// <inheritdoc />
        public override bool TryParseResult(string result, out RecomputeContentHashEntry commandResult)
        {
            try
            {
                using (var stream = new MemoryStream(Convert.FromBase64String(result)))
                using (var reader = new BinaryReader(stream))
                {
                    commandResult = RecomputeContentHashEntry.Deserialize(reader);
                    return true;
                }
            }
            catch (Exception)
            {
                commandResult = null;
                throw;
            }
        }

        internal override void InternalSerialize(BinaryWriter writer)
        {
            writer.Write(File.Path.RawValue);
            writer.Write(File.RewriteCount);
            writer.Write(RequestedHashType);
            Entry.Serialize(writer);
        }

        internal static Command InternalDeserialize(BinaryReader reader)
        {
            var file = new FileArtifact(
                new AbsolutePath(reader.ReadInt32()),
                reader.ReadInt32());
            string requestedhashType = reader.ReadString();
            return new RecomputeContentHashCommand(file, requestedhashType, RecomputeContentHashEntry.Deserialize(reader));
        }
    }
}
