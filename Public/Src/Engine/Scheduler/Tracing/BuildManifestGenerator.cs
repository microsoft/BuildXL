// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Ipc.ExternalApi.Commands;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

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
        private volatile bool m_generateBuildManifestFileListInvoked = false;

        /// <summary>
        /// Counters for all BuildManifest related statistics.
        /// </summary>
        public static readonly CounterCollection<BuildManifestCounters> Counters = new CounterCollection<BuildManifestCounters>();

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
        /// Key is a tuple of (DropName, RelativePath) for each individual file added to the drop specified
        /// </summary>
        internal readonly ConcurrentBigMap<(StringId DropName, RelativePath RelativePath), BuildManifestHashes> BuildManifestEntries;

        /// <summary>
        /// Constructor.
        /// </summary>
        public BuildManifestGenerator(
            LoggingContext loggingContext,
            StringTable stringTable)
        {
            Contract.Requires(loggingContext != null);

            m_loggingContext = loggingContext;
            m_stringTable = stringTable;
            m_duplicateEntries = new ConcurrentBag<(string, string, string, string)>();
            BuildManifestEntries = new ConcurrentBigMap<(StringId, RelativePath), BuildManifestHashes>();
        }

        /// <summary>
        /// Record details of a file added to drop.
        /// </summary>
        public void RecordFileForBuildManifest(List<BuildManifestEntry> records)
        {
            Contract.Requires(records != null, "Build Manifest Records can't be null");

            using (Counters.StartStopwatch(BuildManifestCounters.ReceiveRecordFileForBuildManifestEventOnMasterDuration))
            {
                foreach (var record in records)
                {
                    if (m_generateBuildManifestFileListInvoked)
                    {
                        Logger.Log.RecordFileForBuildManifestAfterGenerateBuildManifestFileList(m_loggingContext,
                            record.DropName,
                            record.RelativePath,
                            record.AzureArtifactsHash.Serialize(),
                            record.BuildManifestHash.Serialize());
                    }

                    Counters.IncrementCounter(BuildManifestCounters.TotalRecordFileForBuildManifestCalls);

                    RelativePath relativePathObj = RelativePath.Create(m_stringTable, record.RelativePath);
                    StringId dropNameId = StringId.Create(m_stringTable, record.DropName);

                    using (Counters.StartStopwatch(BuildManifestCounters.AddHashesToBuildManifestEntriesDuration))
                    {
                        var existingEntry = BuildManifestEntries.GetOrAdd(
                            (dropNameId, relativePathObj),
                            new BuildManifestHashes(record.AzureArtifactsHash, record.BuildManifestHash));

                        if (existingEntry.IsFound &&
                            !record.AzureArtifactsHash.Equals(existingEntry.Item.Value.AzureArtifactsHash))
                        {
                            m_duplicateEntries.Add((record.DropName, record.RelativePath, existingEntry.Item.Value.AzureArtifactsHash.Serialize(), record.AzureArtifactsHash.Serialize()));
                        }
                        else
                        {
                            Counters.IncrementCounter(BuildManifestCounters.UniqueRecordFileForBuildManifestCalls);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Rejects duplicate Hash registerations and generates a list of <see cref="BuildManifestFileInfo"/>.
        /// </summary>
        /// <param name="dropName">Drop Name</param>
        /// <param name="error">Nullable error message</param>
        /// <param name="buildManifestFileInfoList">Nullable buildManifestFileInfoList</param>
        /// <returns>Logs an error and returns false if duplicate file registeration attempts are detected.</returns>
        public bool TryGenerateBuildManifestFileList(string dropName, out string error, out List<BuildManifestFileInfo> buildManifestFileInfoList)
        {
            m_generateBuildManifestFileListInvoked = true;

            var duplicateEntries = DuplicateEntries(dropName);
            if (duplicateEntries.Count != 0)
            {
                Logger.Log.GenerateBuildManifestFileListFoundDuplicateHashes(m_loggingContext, dropName, duplicateEntries.Count);

                foreach (var dup in duplicateEntries)
                {
                    Logger.Log.BuildManifestGeneratorFoundDuplicateHash(m_loggingContext, dropName, dup.relativePath, dup.recordedHash, dup.rejectedHash);
                }

                error = $"Operation Register BuildManifest Hash for Drop '{dropName}' failed due to {duplicateEntries.Count} files with mismatching hashes being registered at respective RelativePaths.";
                buildManifestFileInfoList = null;
                return false;
            }

            using (Counters.StartStopwatch(BuildManifestCounters.GenerateBuildManifestFileListDuration))
            {
                StringId dropStringId = StringId.Create(m_stringTable, dropName);

                List<BuildManifestFileInfo> sortedManifestDetailsForDrop = BuildManifestEntries
                    .Where(kvp => kvp.Key.DropName == dropStringId)
                    .Select(kvp => (relPathStr: kvp.Key.RelativePath.ToString(m_stringTable), hashes: kvp.Value))
                    .OrderBy(t => t.relPathStr)
                    .Select(t => ToBuildManifestDataComponent(
                        t.relPathStr,
                        t.hashes.AzureArtifactsHash.ToHex(),
                        t.hashes.BuildManifestHash.ToHex()))
                    .ToList();

                Logger.Log.GenerateBuildManifestFileListResult(m_loggingContext, dropName, sortedManifestDetailsForDrop.Count);

                buildManifestFileInfoList = sortedManifestDetailsForDrop;
                error = null;
                return true;
            }
        }

        private BuildManifestFileInfo ToBuildManifestDataComponent(string relativePath, string azureArtifactsHash, string buildManifestHash)
        {
            return new BuildManifestFileInfo(
                relativePath.Replace('\\', '/').Replace("//", "/"),
                azureArtifactsHash,
                buildManifestHash
            );
        }
    }

    /// <summary>
    /// Build Manifest individual XLG registration entry
    /// </summary>
    public readonly struct BuildManifestEntry
    {
        /// <nodoc/>
        public string DropName { get; }

        /// <nodoc/>
        public string RelativePath { get; }

        /// <nodoc/>
        public ContentHash AzureArtifactsHash { get; }

        /// <nodoc/>
        public ContentHash BuildManifestHash { get; }

        /// <nodoc/>
        public BuildManifestEntry(
            string dropName,
            string relativePath,
            ContentHash azureArtifactsHash,
            ContentHash buildManifestHash)
        {
            DropName = dropName;
            RelativePath = relativePath;
            AzureArtifactsHash = azureArtifactsHash;
            BuildManifestHash = buildManifestHash;
        }

        /// <nodoc/>
        public bool IsValid => !string.IsNullOrEmpty(DropName) && !string.IsNullOrEmpty(RelativePath) && AzureArtifactsHash.IsValid && BuildManifestHash.IsValid;
    }

    /// <summary>
    /// Hashes to be stored into the Build Manifest for each individual XLG registration
    /// </summary>
    public readonly struct BuildManifestHashes
    {
        /// <nodoc/>
        public ContentHash AzureArtifactsHash { get; }

        /// <nodoc/>
        public ContentHash BuildManifestHash { get; }

        /// <nodoc/>
        public BuildManifestHashes(ContentHash azureArtifactsHash, ContentHash buildManifestHash)
        {
            AzureArtifactsHash = azureArtifactsHash;
            BuildManifestHash = buildManifestHash;
        }
    }

    /// <summary>
    /// Counter types for all BuildManifest related statistics.
    /// </summary>
    public enum BuildManifestCounters
    {
        /// <summary>
        /// Time spent storing hashes for build manifest
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ReceiveRecordFileForBuildManifestEventOnMasterDuration,

        /// <summary>
        /// Number of <see cref="BuildManifestGenerator.RecordFileForBuildManifest"/> calls
        /// </summary>
        [CounterType(CounterType.Numeric)]
        TotalRecordFileForBuildManifestCalls,

        /// <summary>
        /// Number of <see cref="BuildManifestGenerator.RecordFileForBuildManifest"/> calls that were added into <see cref="BuildManifestGenerator.BuildManifestEntries"/>
        /// </summary>
        [CounterType(CounterType.Numeric)]
        UniqueRecordFileForBuildManifestCalls,

        /// <summary>
        /// Time spent generating the <see cref="BuildManifestGenerator.TryGenerateBuildManifestFileList"/>
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        GenerateBuildManifestFileListDuration,

        /// <summary>
        /// Time spent adding <see cref="BuildManifestHashes"/> into <see cref="BuildManifestGenerator.BuildManifestEntries"/>
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        AddHashesToBuildManifestEntriesDuration,
    }
}
