// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Ipc.ExternalApi
{
    /// <summary>
    /// A struct containing all the data required for Build Manifest generation in DropDaemon
    /// </summary>
    public readonly struct BuildManifestData
    {
        /// <summary>
        /// Used for separating rendered values in <see cref="ToString"/>
        /// </summary>
        internal const char Separator = '|';

        /// <summary>
        /// Indicates Build Manifest Version
        /// </summary>
        public readonly string Version;

        /// <summary>
        /// Unix Time the Build Manifest data was captured
        /// </summary>
        public readonly long Timestamp;

        /// <summary>
        /// List representing data of all files uploaded to Drop
        /// </summary>
        public readonly IEnumerable<BuildManifestFile> Outputs;

        /// <nodoc/>
        public BuildManifestData(
            string version,
            long timestamp,
            IEnumerable<BuildManifestFile> outputs)
        {
            Contract.Requires(!string.IsNullOrEmpty(version));
            Contract.Requires(timestamp != 0);
            Contract.Requires(outputs != null);

            Version = version;
            Timestamp = timestamp;
            Outputs = outputs;
        }

        /// <summary>
        /// Parses a string and forms a <see cref="BuildManifestData"/> struct. The string must follow the format specified by <see cref="ToString"/> method.
        /// </summary>
        public static bool TryParse(string value, out BuildManifestData data)
        {
            int numberOfObjectsWithinBuildManifestData = 3;
            data = default;

            string[] splits = value.Split(new[] { Separator }, StringSplitOptions.None);

            bool valid = splits.Length >= numberOfObjectsWithinBuildManifestData &&
                (splits.Length - numberOfObjectsWithinBuildManifestData) % 3 == 0;
            if (!valid)
            {
                return false;
            }

            string version      = splits[0];

            if (!long.TryParse(splits[1], out long timestamp))
            {
                return false;
            }

            List<BuildManifestFile> outputs = new List<BuildManifestFile>();
            
            if(!int.TryParse(splits[2], out var outputLen))
            {
                return false;
            }

            if (outputLen * 3 != splits.Length - numberOfObjectsWithinBuildManifestData)
            {
                return false;
            }

            for (int i = numberOfObjectsWithinBuildManifestData; i < splits.Length; i += 3)
            {
                outputs.Add(new BuildManifestFile(
                    splits[i],
                    splits[i+1],
                    splits[i+2]));
            }

            data = new BuildManifestData(version,
                timestamp,
                outputs);

            return true;
        }

        /// <nodoc/>
        public override bool Equals(object obj)
        {
            if ((obj == null) || !this.GetType().Equals(obj.GetType()))
            {
                return false;
            }
            else
            {
                BuildManifestData data = (BuildManifestData)obj;
                return data.Version.Equals(Version) &&
                    data.Timestamp.Equals(Timestamp) &&
                    Enumerable.SequenceEqual(data.Outputs, Outputs);
            }
        }

        /// <nodoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(
                Version.GetHashCode(),
                Timestamp.GetHashCode(),
                HashCodeHelper.Combine(Outputs.ToArray(), o => o.GetHashCode()));
        }

        /// <summary>
        /// Serializes a BuildManifestData into a string identifier.
        /// Format: {Version}|{Timestamp}|{Outputs.Count}|{RelativePath}|{AzureArtifactsHash}|{BuildManifestHash}|...
        /// </summary>
        public override string ToString()
        {
            string values = I($"{Version}{Separator}{Timestamp}");

            StringBuilder outputList = new StringBuilder();

            foreach (BuildManifestFile file in Outputs)
            {
                outputList.Append(I($"{Separator}{file.RelativePath}{Separator}{file.AzureArtifactsHash}{Separator}{file.BuildManifestHash}"));
            }

            return I($"{values}{Separator}{Outputs.Count()}{outputList}");
        }
    }

    /// <summary>
    /// A struct representing paths and hashes of files uploaded to drop
    /// </summary>
    public readonly struct BuildManifestFile
    {
        /// <summary>
        /// Relative path of the file within the Drop
        /// </summary>
        public string RelativePath { get; }

        /// <summary>
        /// VSO Content Hash of the file
        /// </summary>
        public string AzureArtifactsHash { get; }

        /// <summary>
        /// SHA-256 Content Hash of the File
        /// </summary>
        public string BuildManifestHash { get; }

        /// <nodoc/>
        public BuildManifestFile(
            string relativePath,
            string azureArtifactsHash,
            string buildManifestHash)
        {
            RelativePath = relativePath;
            AzureArtifactsHash = azureArtifactsHash;
            BuildManifestHash = buildManifestHash;
        }

        /// <nodoc/>
        public override bool Equals(object obj)
        {
            if ((obj == null) || !this.GetType().Equals(obj.GetType()))
            {
                return false;
            }
            else
            {
                BuildManifestFile file = (BuildManifestFile)obj;
                return file.RelativePath.Equals(RelativePath) &&
                    file.AzureArtifactsHash.Equals(AzureArtifactsHash) &&
                    file.BuildManifestHash.Equals(BuildManifestHash);
            }
        }

        /// <nodoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(
                RelativePath.GetHashCode(),
                AzureArtifactsHash.GetHashCode(),
                BuildManifestHash.GetHashCode());
        }
    }
}