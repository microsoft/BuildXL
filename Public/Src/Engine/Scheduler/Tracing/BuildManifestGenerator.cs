// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Ipc.ExternalApi.Commands;
using BuildXL.Utilities.Core;
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
        private readonly ConcurrentBag<(string dropName, string relativePath, string recordedHash, string rejectedHash)> m_duplicateEntries = new();
        private readonly ConcurrentDictionary<string, bool> m_dropManifestFinalizations = new();

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
        /// Details of files added to drop. The keys are the string ids to a drop's name, 
        /// i.e., we create one mapping of RelativePath to its hashes per drop.
        /// </summary>
        internal readonly ConcurrentDictionary<StringId, ConcurrentBigMap<RelativePath, BuildManifestHashes>> BuildManifestEntries = new();
        private readonly IEqualityComparer<RelativePath> m_relativePathCaseInsensitiveComparer;

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
            m_relativePathCaseInsensitiveComparer = new CaseInsensitiveComparer(m_stringTable);
        }

        /// <summary>
        /// Record details of files added to drop.
        /// </summary>
        public void RecordFileForBuildManifest(List<BuildManifestEntry> records)
        {
            Contract.Requires(records != null, "Build Manifest Records can't be null");
            Contract.Requires(records.Count > 0, "Build Manifest Records can't be empty");

            using (Counters.StartStopwatch(BuildManifestCounters.ReceiveRecordFileForBuildManifestEventOnOrchestratorDuration))
            {
                // Currently, each invocation of this method can essentially be matched to a call received from DropDaemon.
                // In turn, in each call, DropDaemon only puts files that belong to the same drop.
                // This check here is mainly to ensure that the upstream behavior has not changed.
                var dropName = records[0].DropName;
                for (int i = 1; i < records.Count; i++)
                {
                    Contract.Assert(dropName == records[i].DropName, $"All records must be from the same drop. Mismatched drop names: '{dropName}', '{records[i].DropName}'");
                }

                if (m_dropManifestFinalizations.ContainsKey(dropName))
                {
                    // only log one file per batch
                    Logger.Log.RecordFileForBuildManifestAfterGenerateBuildManifestFileList(
                        m_loggingContext,
                        records.Count,
                        records[0].DropName,
                        records[0].RelativePath,
                        records[0].AzureArtifactsHash.Serialize());
                }

                var dropNameId = StringId.Create(m_stringTable, dropName);
                var entries = BuildManifestEntries.GetOrAdd(dropNameId, _ => new(keyComparer: m_relativePathCaseInsensitiveComparer));

                foreach (var record in records)
                {
                    Counters.IncrementCounter(BuildManifestCounters.TotalRecordFileForBuildManifestCalls);

                    RelativePath relativePathObj = RelativePath.Create(m_stringTable, record.RelativePath);

                    using (Counters.StartStopwatch(BuildManifestCounters.AddHashesToBuildManifestEntriesDuration))
                    {
                        var existingEntry = entries.GetOrAdd(relativePathObj, new BuildManifestHashes(record.AzureArtifactsHash, record.BuildManifestHashes));

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
        /// Rejects duplicate Hash registrations and generates a list of <see cref="BuildManifestFileInfo"/>.
        /// </summary>
        /// <param name="dropName">Drop Name</param>
        /// <param name="error">Nullable error message</param>
        /// <param name="buildManifestFileInfoList">Nullable buildManifestFileInfoList</param>
        /// <returns>Logs an error and returns false if duplicate file registration attempts are detected.</returns>
        public bool TryGenerateBuildManifestFileList(string dropName, out string error, out List<BuildManifestFileInfo> buildManifestFileInfoList)
        {
            m_dropManifestFinalizations[dropName] = true;

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

                List<BuildManifestFileInfo> sortedManifestDetailsForDrop;

                if (BuildManifestEntries.TryGetValue(dropStringId, out var entries)) 
                {
                    sortedManifestDetailsForDrop = entries
                        .Select(kvp => (relPathStr: kvp.Key.ToString(m_stringTable), hashes: kvp.Value))
                        .OrderBy(t => t.relPathStr)
                        .Select(t => ToBuildManifestDataComponent(t.relPathStr, t.hashes.AzureArtifactsHash, t.hashes.Hashes))
                        .ToList();
                }
                else
                {
                    // Empty drop
                    sortedManifestDetailsForDrop = new();
                }

                Logger.Log.GenerateBuildManifestFileListResult(m_loggingContext, dropName, sortedManifestDetailsForDrop.Count);

                buildManifestFileInfoList = sortedManifestDetailsForDrop;
                error = null;
                return true;
            }
        }

        private BuildManifestFileInfo ToBuildManifestDataComponent(string relativePath, ContentHash azureArtifactsHash, IReadOnlyList<ContentHash> buildManifestHashes)
        {
            return new BuildManifestFileInfo(
                relativePath.Replace('\\', '/').Replace("//", "/"),
                azureArtifactsHash,
                buildManifestHashes
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
        public IReadOnlyList<ContentHash> BuildManifestHashes { get; }

        /// <nodoc/>
        public BuildManifestEntry(
            string dropName,
            string relativePath,
            ContentHash azureArtifactsHash,
            IReadOnlyList<ContentHash> buildManifestHashes)
        {
            DropName = dropName;
            RelativePath = relativePath;
            AzureArtifactsHash = azureArtifactsHash;
            BuildManifestHashes = buildManifestHashes;
        }

        /// <nodoc/>
        public bool IsValid => !string.IsNullOrEmpty(DropName) && !string.IsNullOrEmpty(RelativePath) && AzureArtifactsHash.IsValid && BuildManifestHashes.All(t => t.IsValid);
    }

    /// <summary>
    /// Hashes to be stored into the Build Manifest for each individual XLG registration
    /// </summary>
    public readonly struct BuildManifestHashes
    {
        /// <nodoc/>
        public ContentHash AzureArtifactsHash { get; }

        /// <nodoc/>
        public IReadOnlyList<ContentHash> Hashes { get; }

        /// <nodoc/>
        public BuildManifestHashes(ContentHash azureArtifactsHash, IReadOnlyList<ContentHash> buildManifestHashes)
        {
            AzureArtifactsHash = azureArtifactsHash;
            Hashes = buildManifestHashes;
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
        ReceiveRecordFileForBuildManifestEventOnOrchestratorDuration,

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

    /// <summary>
    /// A comparer that compare two relativepath object in a case insensitive manner
    /// </summary>
    internal sealed class CaseInsensitiveComparer : IEqualityComparer<RelativePath>
    {
        private readonly StringTable m_stringTable;

        /// <nodoc/>
        public CaseInsensitiveComparer(StringTable stringTable)
        {
            Contract.RequiresNotNull(stringTable);
            m_stringTable = stringTable;
        }

        public bool Equals(RelativePath path, RelativePath other) => path.CaseInsensitiveEquals(m_stringTable, other);

        public int GetHashCode(RelativePath relativePath) => HashCodeHelper.Combine(relativePath.Components, m_stringTable.CaseInsensitiveGetHashCode);
    }
}
