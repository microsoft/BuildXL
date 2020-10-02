// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Execution log events that need to be intercepted only on non-worker machines.
    /// </summary>
    public sealed class BuildManifestStoreTarget : ExecutionLogTargetBase
    {
        private readonly StringTable m_stringTable;

        /// <summary>
        /// Details of files added to drop.
        /// </summary>
        public readonly ConcurrentBigSet<BuildManifestEntry> BuildManifestEntries;

        /// <summary>
        /// Handle the events from workers
        /// </summary>
        public override bool CanHandleWorkerEvents => true;

        /// <summary>
        /// Constructor.
        /// </summary>
        public BuildManifestStoreTarget(StringTable stringTable)
        {
            BuildManifestEntries = new ConcurrentBigSet<BuildManifestEntry>();
            m_stringTable = stringTable;
        }

        /// <inheritdoc/>
        public override void RecordFileForBuildManifest(RecordFileForBuildManifestEventData data)
        {
            BuildManifestEntries.Add(new BuildManifestEntry(
                StringId.Create(m_stringTable, data.DropName),
                RelativePath.Create(m_stringTable, data.RelativePath),
                data.BuildManifestHash));
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
            public ContentHash BuildManifestHash { get; }

            /// <nodoc/>
            public BuildManifestEntry(StringId dropName, RelativePath relativePath, ContentHash buildManifestHash)
            {
                DropName = dropName;
                RelativePath = relativePath;
                BuildManifestHash = buildManifestHash;
            }
        }
    }
}
