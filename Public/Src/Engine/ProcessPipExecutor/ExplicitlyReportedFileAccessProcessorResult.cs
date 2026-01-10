// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Utilities.Core;

namespace BuildXL.ProcessPipExecutor
{
    /// <summary>
    /// The result of processing explicitly reported file accesses. See <see cref="ExplicitlyReportedFileAccessProcessor"/>
    /// </summary>
    /// <remarks>
    /// This class is disposable because it may contain pooled collections that need to be returned to their pools.
    /// The exposed collections are mutable (despite being a 'result') because they are later consumed by other components that may modify them.
    /// </remarks>
    public sealed class ExplicitlyReportedFileAccessProcessorResult : IDisposable
    {
        private readonly PooledObjectWrapper<Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>> m_accessesAndFlagsByPathWrapper = ProcessPools.ObservedFileAccessesAndFlagsByPathPool.GetInstance();
        private readonly PooledObjectWrapper<HashSet<AbsolutePath>> m_createdDirectoriesMutableWrapper = Pools.GetAbsolutePathSet();
        private readonly PooledObjectWrapper<Dictionary<AbsolutePath, HashSet<AbsolutePath>>> m_dynamicWriteAccessWrapper = ProcessPools.DynamicWriteAccesses.GetInstance();
        private readonly PooledObjectWrapper<HashSet<AbsolutePath>> m_maybeUnresolvedAbsentAccessessWrapper = Pools.GetAbsolutePathSet();
        private readonly PooledObjectWrapper<HashSet<AbsolutePath>> m_fileExistenceDenialsWrapper = Pools.GetAbsolutePathSet();
        private readonly PooledObjectWrapper<SortedDictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>> m_sortedObservationsByPathWrapper;

        /// <nodoc/>
        public Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable> AccessesByPath { get; }
        /// <nodoc/>
        public SortedDictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable> SortedObservationsByPath { get; }
        /// <nodoc/>
        public HashSet<AbsolutePath> CreatedDirectories { get; }
        /// <nodoc/>
        public Dictionary<AbsolutePath, HashSet<AbsolutePath>> DynamicWriteAccesses { get; }
        /// <nodoc/>
        public HashSet<AbsolutePath> FileExistenceDenials { get; }
        /// <nodoc/>
        public HashSet<AbsolutePath> MaybeUnresolvedAbsentAccesses { get; }

        /// <nodoc/>
        public ExplicitlyReportedFileAccessProcessorResult(PathTable pathTable)
        {
            AccessesByPath = m_accessesAndFlagsByPathWrapper.Instance;
            m_sortedObservationsByPathWrapper = ProcessPools.GetSortedObservationsByPath(pathTable.ExpandedPathComparer).GetInstance();
            SortedObservationsByPath = m_sortedObservationsByPathWrapper.Instance;
            CreatedDirectories = m_createdDirectoriesMutableWrapper.Instance;
            DynamicWriteAccesses = m_dynamicWriteAccessWrapper.Instance;
            FileExistenceDenials = m_fileExistenceDenialsWrapper.Instance;
            MaybeUnresolvedAbsentAccesses = m_maybeUnresolvedAbsentAccessessWrapper.Instance;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_accessesAndFlagsByPathWrapper.Dispose();
            m_createdDirectoriesMutableWrapper.Dispose();
            m_dynamicWriteAccessWrapper.Dispose();
            m_maybeUnresolvedAbsentAccessessWrapper.Dispose();
            m_fileExistenceDenialsWrapper.Dispose();
            m_sortedObservationsByPathWrapper.Dispose();
        }
    }
}
