// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using BuildXL.Native.IO;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Provides funcionality to identify whether paths were created/modified after the BuildXL engine has started
    /// </summary>
    /// <remarks>
    /// This is useful for making raw distinctions between files that existed before BuildXL started (potential sources)
    /// and outputs produced by the build.
    /// </remarks>
    public sealed class FileTimestampTracker
    {
        private readonly DateTime m_startOfBuildTimestamp;

        private readonly ConcurrentBigMap<AbsolutePath, DateTime?> m_creationTimeCache = new();
        private readonly PathTable m_pathTable;

        /// <summary>
        /// Whether file creation tracking is supported
        /// </summary>
        /// <remarks>
        /// This is OS dependent
        /// </remarks>
        public bool IsFileCreationTrackingSupported { get; }

        /// <nodoc/>
        public FileTimestampTracker(DateTime engineStartTimeInUtc, PathTable pathTable)
        {
            m_pathTable = pathTable;
            m_startOfBuildTimestamp = engineStartTimeInUtc;

            // On Linux, not all systems support tracking file creation time. In those cases, the creation time comes back
            // as the UnixEpoch time
            IsFileCreationTrackingSupported = FileUtilities.SupportsCreationDate();
        }

        /// <summary>
        /// Whether the given path was created after the engine started
        /// </summary>
        /// <remarks>
        /// Consider that bxl produced outputs that are hardlinked from the cache may have a creation date that does not correspond
        /// to the expected creation date, since hardlinks reflect the dates of their targets.
        /// The behavior is not defined if <see cref="IsFileCreationTrackingSupported"/> is false.
        /// The value of retrieved is cached under the assumption that creation times don't change.
        /// </remarks>
        public bool PathCreatedAfterEngineStarted(AbsolutePath path)
        {
            var creationTime = RetrieveCreationTimeWithCache(path);

            // if no timestamps were retrieved, that's an indication the path is absent, and in that case we return false
            // since the path is not created at all
            return creationTime.HasValue && m_startOfBuildTimestamp.IsLessThan(creationTime.Value);
        }

        /// <summary>
        /// Whether the given path was modified after the engine started
        /// </summary>
        /// <remarks>
        /// Consider that bxl produced outputs that are hardlinked from the cache may have a modification date that does not correspond
        /// to the expected modification date, since hardlinks reflect the dates from their targets.
        /// </remarks>
        public bool PathModifiedAfterEngineStarted(AbsolutePath path)
        {
            var lastChange = GetTimestampIfAvailable(path)?.LastChangeTime;

            // if no timestamps were retrieved, that's an indication the path is absent, and in that case we return false
            // since the path is not modified at all
            return lastChange.HasValue && m_startOfBuildTimestamp.IsLessThan(lastChange.Value);
        }

        /// <summary>
        /// Associates an artificial timestamp to the given path for testing purposes
        /// </summary>
        /// <remarks>
        /// On Linux there is not a clear way to alter the birth time of a file
        /// </remarks>
        public void InjectArtificialCreationTimestampForTesting(AbsolutePath path, DateTime creationTime)
        {
            m_creationTimeCache.AddOrUpdate(path, this, (key, instance) => creationTime, (key, tms, instance) => creationTime);
        }

        private DateTime? RetrieveCreationTimeWithCache(AbsolutePath path)
        {
            return m_creationTimeCache.GetOrAdd(path, this, (path, identifier) => GetTimestampIfAvailable(path)?.CreationTime).Item.Value;
        }

        private FileTimestamps? GetTimestampIfAvailable(AbsolutePath path)
        {
            try
            {
                return FileUtilities.GetFileTimestamps(path.ToString(m_pathTable));
            }
            catch (BuildXLException)
            {
                // Use null to represent that that we couldn't retrieve the creation time.
                // One reason for this exception is that the path is absent, in which case is safe that all queries 
                // for path modified/created after the engine started return false. For the other cases (access denied, etc),
                // we pretend that the path is absent as well. If the path actually needs to be read, the issue is going to get
                // handled organically downstream
                return null;
            }
        }
    }
}
