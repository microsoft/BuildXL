// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Classes and functions used for Build Manifest Generation.
    /// </summary>
    public sealed class BuildManifestGenerator
    {
        private readonly StringTable m_stringTable;
        private readonly LoggingContext m_loggingContext;

        /// <summary>
        /// Details of files added to drop.
        /// Key is a tuple of DropName and RelativePath of the file within the drop
        /// </summary>
        internal readonly ConcurrentBigMap<(StringId, RelativePath), BuildManifestEntry> BuildManifestEntries;

        /// <summary>
        /// Indicates Build Manifest Version
        /// </summary>
        public readonly string Version;

        /// <summary>
        /// Constructor.
        /// </summary>
        public BuildManifestGenerator(
            LoggingContext loggingContext,
            StringTable stringTable,
            string version = "1.0.0")
        {
            Contract.Requires(loggingContext != null);

            m_loggingContext = loggingContext;
            m_stringTable = stringTable;
            Version = version;
            BuildManifestEntries = new ConcurrentBigMap<(StringId, RelativePath), BuildManifestEntry>();
        }

        /// <summary>
        /// Record details of a file added to drop.
        /// </summary>
        public void RecordFileForBuildManifest(
            string dropName,
            string relativePath,
            ContentHash azureArtifactsHash,
            ContentHash buildManifestHash)
        {
            RelativePath relativePathObj = RelativePath.Create(m_stringTable, relativePath);
            StringId dropNameId = StringId.Create(m_stringTable, dropName);

            var existingEntry = BuildManifestEntries.GetOrAdd((dropNameId, relativePathObj), new BuildManifestEntry(
                dropNameId,
                relativePathObj,
                azureArtifactsHash,
                buildManifestHash));

            if (existingEntry.IsFound && 
                !azureArtifactsHash.Equals(existingEntry.Item.Value.AzureArtifactsHash))
            {
                Logger.Log.ApiServerRegisterBuildManifestHashFoundDuplicateEntry(m_loggingContext,
                    dropName,
                    relativePath,
                    existingEntry.OldItem.Value.AzureArtifactsHash.Serialize(),
                    azureArtifactsHash.Serialize());
            }

            Logger.Log.ApiServerForwardedIpcServerMessage(m_loggingContext, "Verbose", $"RecordFileForBuildManifest added a file at RelativePath '{relativePath}'. AzureArtifactsHash: '{azureArtifactsHash.Serialize()}'. BuildManifestHash: '{buildManifestHash.Serialize()}'");
        }

        /// <summary>
        /// Generate a Build Manifest.
        /// </summary>
        public BuildManifestData GenerateBuildManifestData(string dropName)
        {
            StringId dropStringId = StringId.Create(m_stringTable, dropName);
            List<BuildManifestFile> sortedManifestDetailsForDrop = BuildManifestEntries.Values
                .Where(bme => bme.DropName == dropStringId)
                .Select(bme => (relPathStr: bme.RelativePath.ToString(m_stringTable), bme: bme))
                .OrderBy(t => t.relPathStr)
                .Select(t => ToBuildManifestDataComponent(t.relPathStr,
                    t.bme.AzureArtifactsHash.Serialize(),
                    t.bme.BuildManifestHash.Serialize()))
                .ToList();

            return new BuildManifestData(
                Version,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                sortedManifestDetailsForDrop
            );
        }

        private BuildManifestFile ToBuildManifestDataComponent(string relativePath, string azureArtifactsHash, string buildManifestHash)
        {
            return new BuildManifestFile(
                relativePath.Replace('\\', '/'),
                azureArtifactsHash,
                buildManifestHash
            );
        }

        /// <summary>
        /// Represents individual files added to drop
        /// </summary>
        public struct BuildManifestEntry
        {
            /// <nodoc/>
            public StringId DropName { get; }

            /// <nodoc/>
            public RelativePath RelativePath { get; }

            /// <nodoc/>
            public ContentHash AzureArtifactsHash { get; }

            /// <nodoc/>
            public ContentHash BuildManifestHash { get; }

            /// <nodoc/>
            public BuildManifestEntry(
                StringId dropName,
                RelativePath relativePath,
                ContentHash azureArtifactsHash,
                ContentHash buildManifestHash)
            {
                DropName = dropName;
                RelativePath = relativePath;
                AzureArtifactsHash = azureArtifactsHash;
                BuildManifestHash = buildManifestHash;
            }
        }
    }
}
