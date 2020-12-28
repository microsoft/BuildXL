// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using Microsoft.ManifestGenerator;

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

        /// <summary>
        /// Git Repo location.
        /// </summary>
        public string Repo { get; }

        /// <summary>
        /// Git Branch name.
        /// </summary>
        public string Branch { get; }

        /// <summary>
        /// Commit id of current <see cref="Branch"/> head.
        /// </summary>
        public string CommitId { get; }

        /// <summary>
        /// Relative Activity Id or CloudBuildId for the build.
        /// </summary>
        public string CloudBuildId { get; }

        /// <nodoc />
        public GenerateBuildManifestDataCommand(
            string dropName,
            string repo,
            string branch,
            string commitId,
            string cloudBuildId)
        {
            DropName = dropName;
            Repo = repo;
            Branch = branch;
            CommitId = commitId;
            CloudBuildId = cloudBuildId;
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
            writer.Write(Repo);
            writer.Write(Branch);
            writer.Write(CommitId);
            writer.Write(CloudBuildId);
        }

        internal static Command InternalDeserialize(BinaryReader reader)
        {
            string dropName = reader.ReadString();
            string repo = reader.ReadString();
            string branch = reader.ReadString();
            string commitId = reader.ReadString();
            string cloudBuildId = reader.ReadString();

            return new GenerateBuildManifestDataCommand(
                dropName: dropName,
                repo: repo,
                branch : branch,
                commitId: commitId,
                cloudBuildId : cloudBuildId);
        }
    }
}
