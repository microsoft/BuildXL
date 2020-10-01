// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Storage
{
    /// <summary>
    /// Counters for <see cref="FileContentTable" />.
    /// </summary>
    public enum FileContentTableCounters
    {
        /// <summary>
        /// The number of files that are not found in the FileContentTable
        /// </summary>
        NumFileIdMismatch,

        /// <summary>
        /// The number of files whose usn numbers are changed but not the content.
        /// </summary>
        NumUsnMismatch,

        /// <summary>
        /// The number of files whose content is changed.
        /// </summary>
        NumContentMismatch,

        /// <summary>
        /// Number of entries
        /// </summary>
        NumEntries,

        /// <summary>
        /// Number of entries that are deleted during serialization
        /// </summary>
        NumEvicted,

        /// <summary>
        /// Number of entries whose USNs are updated by journal scanning.
        /// </summary>
        NumUpdatedUsnEntriesByJournalScanning,

        /// <summary>
        /// Number of removed entries by journal scanning.
        /// </summary>
        NumRemovedEntriesByJournalScanning,

        /// <summary>
        /// Number of matched file and USN.
        /// </summary>
        NumHit,

        /// <summary>
        /// Get content hash duration.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        GetContentHashDuration,

        /// <summary>
        /// Record content hash duration.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        RecordContentHashDuration,

        /// <summary>
        /// Time spent saving to disk.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        SaveDuration,
    }
}
