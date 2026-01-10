// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Processes;
using System.Collections.Concurrent;

namespace BuildXL.ProcessPipExecutor
{
    /// <summary>
    /// Object pools for process management types.
    /// </summary>
    public static class ProcessPools
    {
        /// <summary>
        /// Global pool of dictionaries for grouping reported accesses by path.
        /// </summary>
        public static ObjectPool<Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>> ObservedFileAccessesAndFlagsByPathPool { get; } = new ObjectPool<Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>>(
             () => new Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>(),
             s => s.Clear());

        /// <summary>
        /// Global pool of dictionaries for grouping reported writes accesses in dynamic directories
        /// </summary>
        public static ObjectPool<Dictionary<AbsolutePath, HashSet<AbsolutePath>>> DynamicWriteAccesses { get; } = new ObjectPool<Dictionary<AbsolutePath, HashSet<AbsolutePath>>>(
            () => new Dictionary<AbsolutePath, HashSet<AbsolutePath>>(),
            s => s.Clear());

        /// <summary>
        /// Global pool of lists for collecting reported file accesses
        /// </summary>
        public static ObjectPool<List<ReportedFileAccess>> ReportedFileAccessList { get; } = new ObjectPool<List<ReportedFileAccess>>(
            () => new List<ReportedFileAccess>(),
            s => s.Clear());

        private static readonly ConcurrentDictionary<PathTable.ExpandedAbsolutePathComparer, ObjectPool<SortedDictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>>> s_sortedObservationsByPathPools = 
            new ConcurrentDictionary<PathTable.ExpandedAbsolutePathComparer, ObjectPool<SortedDictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>>>();

        /// <summary>
        /// Global pool of sorted dictionaries for grouping reported accesses by path
        /// </summary>
        public static ObjectPool<SortedDictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>> GetSortedObservationsByPath(PathTable.ExpandedAbsolutePathComparer expandedAbsolutePathComparer)
        {
            return s_sortedObservationsByPathPools.GetOrAdd(expandedAbsolutePathComparer,
                _ => new ObjectPool<SortedDictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>>(
                    () => new SortedDictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>(expandedAbsolutePathComparer),
                    s => s.Clear()));
        }
    }
}
