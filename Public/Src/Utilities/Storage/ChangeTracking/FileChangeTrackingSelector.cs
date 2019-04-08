// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Provides filter over set of files which should be tracked
    /// </summary>
    public class FileChangeTrackingSelector
    {
        private readonly FlaggedHierarchicalNameDictionary<bool> m_semanticPathInfoMap;
        private readonly bool m_hasIncludedRoots;
        private readonly bool m_hasExcludedRoots;
        private readonly IFileChangeTrackingSubscriptionSource m_tracker;
        private IFileChangeTrackingSubscriptionSource m_disabledTracker;

        private long m_trackedPathRequestCount;
        private long m_untrackedPathRequestCount;

        /// <summary>
        /// Gets the number of times the selector is called for tracked paths
        /// </summary>
        public long TrackedPathRequestCount => m_trackedPathRequestCount;

        /// <summary>
        /// Gets the number of times the selector is called for untracked paths
        /// </summary>
        public long UntrackedPathRequestCount => m_untrackedPathRequestCount;

        /// <summary>
        /// Creates a new file change tracking filter 
        /// </summary>
        public FileChangeTrackingSelector(
            PathTable pathTable,
            LoggingContext loggingContext,
            IFileChangeTrackingSubscriptionSource tracker,
            IEnumerable<AbsolutePath> includedRoots,
            IEnumerable<AbsolutePath> excludedRoots)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(tracker != null);
            Contract.Requires(includedRoots != null);
            Contract.Requires(excludedRoots != null);

            m_semanticPathInfoMap = new FlaggedHierarchicalNameDictionary<bool>(pathTable, HierarchicalNameTable.NameFlags.Root);
            m_tracker = tracker;
            m_disabledTracker = FileChangeTracker.CreateDisabledTracker(loggingContext);

            foreach (var excludedRoot in excludedRoots)
            {
                if (m_semanticPathInfoMap.TryAdd(excludedRoot.Value, false))
                {
                    m_hasExcludedRoots = true;
                }
            }

            foreach (var includedRoot in includedRoots)
            {
                if (m_semanticPathInfoMap.TryAdd(includedRoot.Value, true))
                {
                    m_hasIncludedRoots = true;
                }
            }
        }

        /// <summary>
        /// Creates a file change tracking filter which allows all paths to be tracked
        /// </summary>
        public static FileChangeTrackingSelector CreateAllowAllFilter(PathTable pathTable, IFileChangeTrackingSubscriptionSource tracker)
        {
            return new FileChangeTrackingSelector(pathTable, Events.StaticContext, tracker, Enumerable.Empty<AbsolutePath>(), Enumerable.Empty<AbsolutePath>());
        }

        /// <summary>
        /// Sets the disabled tracker for unit tests.
        /// </summary>
        protected void SetDisabledTrackerTestOnly(IFileChangeTrackingSubscriptionSource disableTracker)
        {
            m_disabledTracker = disableTracker;
        }

        /// <summary>
        /// Gets the tracker to use for the given path
        /// </summary>
        public IFileChangeTrackingSubscriptionSource GetTracker(AbsolutePath path)
        {
            if (ShouldTrack(path))
            {
                return m_tracker;
            }
            else
            {
                return m_disabledTracker;
            }
        }

        /// <summary>
        /// Gets whether the given path should be tracked for file changes
        /// </summary>
        public bool ShouldTrack(AbsolutePath path)
        {
            if (ShouldTrackCore(path))
            {
                Interlocked.Increment(ref m_trackedPathRequestCount);
                return true;
            }
            else
            {
                Interlocked.Increment(ref m_untrackedPathRequestCount);
                return false;
            }
        }

        private bool ShouldTrackCore(AbsolutePath path)
        {
            if (!m_hasExcludedRoots && !m_hasIncludedRoots)
            {
                // No filtering. All paths are tracked
                return true;
            }

            if (m_semanticPathInfoMap.TryGetFirstMapping(path.Value, out var mapping))
            {
                return mapping.Value;
            }

            // If no included roots, this is an exclusion filter. So all paths are
            // tracked if not explicitly excluded
            bool isExclusionList = !m_hasIncludedRoots;
            return isExclusionList;
        }
    }
}
