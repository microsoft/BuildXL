// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities.Core;

namespace BuildXL.Ipc.ExternalApi
{
    /// <summary>
    /// A struct containing all the data required for registering a file in Build Manifest.
    /// </summary>
    public record BuildManifestEntry
    {
        /// <summary>
        /// Relative File Path of given file inside the drop.
        /// </summary>
        public string RelativePath { get; }

        /// <summary>
        /// Default Content Hash of the file. Generally VSO hash.
        /// </summary>
        public ContentHash Hash { get; }

        /// <summary>
        /// Full file path on disk. Used if file needs to be materialized.
        /// </summary>
        public string FullFilePath { get; }

        /// <summary>
        /// File id.
        /// </summary>
        public FileArtifact Artifact { get; }

        /// <nodoc/>
        public BuildManifestEntry(
            string relativePath,
            ContentHash hash,
            string fullFilePath,
            FileArtifact artifact)
        {
            Contract.Requires(!string.IsNullOrEmpty(relativePath));
            Contract.Requires(!string.IsNullOrEmpty(fullFilePath));

            RelativePath = relativePath;
            Hash = hash;
            FullFilePath = fullFilePath;
            Artifact = artifact;
        }

        internal void Serialize(BinaryWriter writer)
        {
            writer.Write(RelativePath);
            Hash.Serialize(writer);
            writer.Write(FullFilePath);
            writer.Write(Artifact.Path.RawValue);
            writer.Write(Artifact.RewriteCount);
        }

        internal static BuildManifestEntry Deserialize(BinaryReader reader)
        {
            string relativePath = reader.ReadString();
            ContentHash hash = new ContentHash(reader);
            string fullFilePath = reader.ReadString();
            FileArtifact artifact = new FileArtifact(new AbsolutePath(reader.ReadInt32()), reader.ReadInt32());

            return new BuildManifestEntry(relativePath, hash, fullFilePath, artifact);
        }
    }
}