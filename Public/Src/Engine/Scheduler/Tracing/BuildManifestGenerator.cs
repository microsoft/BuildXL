// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Ipc.ExternalApi.Commands;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.ManifestGenerator;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Classes and functions used for Build Manifest Generation.
    /// </summary>
    public sealed class BuildManifestGenerator
    {
        private readonly StringTable m_stringTable;
        private readonly LoggingContext m_loggingContext;
        private readonly ConcurrentBag<(string dropName, string relativePath, string recordedHash, string rejectedHash)> m_duplicateEntries;

        /// <summary>
        /// Records duplicate file registrations for a given relative drop path with mismatching hashes
        /// </summary>
        public IReadOnlyList<(string relativePath, string recordedHash, string rejectedHash)> DuplicateEntries(string dropName) =>
            m_duplicateEntries
                .Where(o => o.dropName == dropName)
                .Select(o => (o.relativePath, o.recordedHash, o.rejectedHash))
                .ToList();

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
            m_duplicateEntries = new ConcurrentBag<(string, string, string, string)>();
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
                m_duplicateEntries.Add((dropName, relativePath, existingEntry.Item.Value.AzureArtifactsHash.Serialize(), azureArtifactsHash.Serialize()));
            }
            else
            {
                Logger.Log.ApiServerForwardedIpcServerMessage(m_loggingContext,
                    "Verbose",
                    $"RecordFileForBuildManifest added a file at RelativePath '{relativePath}'. AzureArtifactsHash: '{azureArtifactsHash.Serialize()}'. BuildManifestHash: '{buildManifestHash.Serialize()}'");
            }
        }

        /// <summary>
        /// Generate a Build Manifest.
        /// </summary>
        public BuildManifestData GenerateBuildManifestData(GenerateBuildManifestDataCommand cmd)
        {
            StringId dropStringId = StringId.Create(m_stringTable, cmd.DropName);
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
                cmd.CloudBuildId,
                cmd.Repo,
                cmd.Branch,
                cmd.CommitId,
                sortedManifestDetailsForDrop);
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
