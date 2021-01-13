// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Utilities;

namespace BuildXL.Ipc.ExternalApi.Commands
{
    /// <summary>
    /// Command corresponding to the <see cref="Client.RegisterFilesForBuildManifest"/> API operation.
    /// </summary>
    public sealed class RegisterFilesForBuildManifestCommand : Command<BuildManifestEntry[]>
    {
        /// <summary>
        /// DropName to identify which drop <see cref="RelativePath"/> belongs to.
        /// </summary>
        public string DropName { get; }

        /// <summary>
        /// Array of BuildManifestEntries to be registered.
        /// </summary>
        public BuildManifestEntry[] BuildManifestEntries { get; }

        /// <nodoc />
        public RegisterFilesForBuildManifestCommand(
            string dropName,
            BuildManifestEntry[] buildManifestEntries)
        {
            DropName = dropName;
            BuildManifestEntries = buildManifestEntries;
        }

        /// <nodoc />
        public RegisterFilesForBuildManifestCommand(
            string dropName,
            List<BuildManifestEntry> buildManifestEntries)
        {
            DropName = dropName;
            BuildManifestEntries = buildManifestEntries.ToArray();
        }

        /// <inheritdoc />
        public override bool TryParseResult(string result, out BuildManifestEntry[] commandResult)
        {
            try
            {
                using (var stream = new MemoryStream(Convert.FromBase64String(result)))
                using (var reader = new BinaryReader(stream))
                {
                    var length = reader.ReadInt32();
                    commandResult = Enumerable.Range(0, length).Select(_ => BuildManifestEntry.Deserialize(reader)).ToArray();
                    return true;
                }
            }
            catch
            {
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                commandResult = new BuildManifestEntry[0];
                return false;
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
            }
        }

        /// <inheritdoc />
        public override string RenderResult(BuildManifestEntry[] commandResult)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(commandResult.Length);
                foreach (var bme in commandResult)
                {
                    bme.Serialize(writer);
                }

                writer.Flush();
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        internal override void InternalSerialize(BinaryWriter writer)
        {
            writer.Write(DropName);
            writer.Write(BuildManifestEntries.Length);
            foreach (BuildManifestEntry buildManifestEntry in BuildManifestEntries)
            {
                buildManifestEntry.Serialize(writer);
            }
        }

        internal static Command InternalDeserialize(BinaryReader reader)
        {
            string dropName = reader.ReadString();
            int entryCount = reader.ReadInt32();

            BuildManifestEntry[] buildManifestEntries = Enumerable
                .Range(0, entryCount)
                .Select(i => BuildManifestEntry.Deserialize(reader))
                .ToArray();

            return new RegisterFilesForBuildManifestCommand(dropName, buildManifestEntries);
        }
    }
}
