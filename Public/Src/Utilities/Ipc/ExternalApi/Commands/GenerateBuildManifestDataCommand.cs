// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;

namespace BuildXL.Ipc.ExternalApi.Commands
{
    /// <summary>
    /// Command corresponding to the <see cref="Client.GenerateBuildManifestData"/> API operation.
    /// </summary>
    public sealed class GenerateBuildManifestDataCommand : Command<BuildManifestData>
    {
        /// <summary>
        /// DropName to identify which drop the Build Manifest is being generated for.
        /// </summary>
        public string DropName { get; }

        /// <nodoc />
        public GenerateBuildManifestDataCommand(string dropName)
        {
            DropName = dropName;
        }

        /// <inheritdoc />
        public override bool TryParseResult(string result, out BuildManifestData commandResult)
        {
            return BuildManifestData.TryParse(result, out commandResult);
        }

        /// <inheritdoc />
        public override string RenderResult(BuildManifestData commandResult)
        {
            return commandResult.ToString();
        }

        internal override void InternalSerialize(BinaryWriter writer)
        {
            writer.Write(DropName);
        }

        internal static Command InternalDeserialize(BinaryReader reader)
        {
            string dropName = reader.ReadString();

            return new GenerateBuildManifestDataCommand(dropName);
        }
    }
}
