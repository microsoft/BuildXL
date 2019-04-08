// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.IncrementalScheduling
{
    /// <summary>
    /// Counters for <see cref="IIncrementalSchedulingState" />.
    /// </summary>
    /// <remarks>
    /// <see cref="IIncrementalSchedulingState"/> uses this adhoc counters for speed reason because these counters are in the hot path.
    /// Using <see cref="CounterCollection"/> may degrade performance.
    /// </remarks>
    public sealed class IncrementalSchedulingStateCounter
    {
        /// <summary>
        /// Number of new files.
        /// </summary>
        public long NewFilesCount;

        /// <summary>
        /// Number of new directories.
        /// </summary>
        public long NewDirectoriesCount;

        /// <summary>
        /// Number of changed files.
        /// </summary>
        public long ChangedFilesCount;

        /// <summary>
        /// Number of changed dynamically observed enumeration memberships.
        /// </summary>
        public long ChangedDynamicallyObservedEnumerationMembershipsCount;

        /// <summary>
        /// Number of changed dynamically observed files.
        /// </summary>
        public long ChangedDynamicallyObservedFilesCount;

        /// <summary>
        /// Number of prepetually dirty nodes.
        /// </summary>
        public long PerpetuallyDirtyNodesCount;

        /// <summary>
        /// Number of nodes marked dirty transitively.
        /// </summary>
        public long NodesTransitivelyDirtiedCount;

        /// <summary>
        /// Number of new files or directories.
        /// </summary>
        public long NewArtifactsCount => NewFilesCount + NewDirectoriesCount;

        /// <summary>
        /// Samples of changes.
        /// </summary>
        public readonly Sample Samples = new Sample();

        /// <summary>
        /// Logs counter.
        /// </summary>
        public void Log(LoggingContext loggingContext)
        {
            Tracing.Logger.Log.IncrementalSchedulingArtifactChangesCounters(
                loggingContext,
                NewFilesCount,
                NewDirectoriesCount,
                ChangedFilesCount,
                ChangedDynamicallyObservedFilesCount,
                ChangedDynamicallyObservedEnumerationMembershipsCount,
                PerpetuallyDirtyNodesCount);

            Samples.Log(loggingContext);
        }

        /// <summary>
        /// Samples of changes.
        /// </summary>
        public class Sample
        {
            /// <summary>
            /// Randomly selected sample size.
            /// </summary>
            private const int SampleSize = 20;

            private readonly List<(string path, PathChanges changes, DynamicObservationType? dynamicObservationType)> m_samples = new List<(string, PathChanges, DynamicObservationType?)>(SampleSize);

            /// <summary>
            /// Adds new file.
            /// </summary>
            public void AddNewFile(string newFile)
            {
                if (m_samples.Count < SampleSize)
                {
                    m_samples.Add((newFile, PathChanges.NewlyPresentAsFile, null));
                }
            }

            /// <summary>
            /// Adds a new directory.
            /// </summary>
            public void AddNewDirectory(string newDirectory)
            {
                if (m_samples.Count < SampleSize)
                {
                    m_samples.Add((newDirectory, PathChanges.NewlyPresentAsDirectory, null));
                }
            }

            /// <summary>
            /// Adds a changed file.
            /// </summary>
            public void AddChangedPath(string changedPath, PathChanges changes)
            {
                if (m_samples.Count < SampleSize)
                {
                    m_samples.Add((changedPath, changes, null));
                }
            }

            /// <summary>
            /// Adds a changed to dynamically observed file or directory.
            /// </summary>
            public void AddChangedDynamicallyObservedArtifact(string changedDynamicallyObservedFile, PathChanges changes, DynamicObservationType dynamicObservationType)
            {
                if (m_samples.Count < SampleSize)
                {
                    m_samples.Add((changedDynamicallyObservedFile, changes, dynamicObservationType));
                }
            }

            /// <summary>
            /// Logs samples.
            /// </summary>
            public void Log(LoggingContext loggingContext)
            {
                string DynamicObservationTypeAsString(DynamicObservationType? dynamicObservationType)
                {
                    return dynamicObservationType.HasValue ? I($"(Dynamic observation type: {dynamicObservationType.Value.ToString()})") : string.Empty;
                }

                string samples = string.Join(
                    Environment.NewLine,
                    m_samples.Select(s => I($"\t{s.path} | {s.changes.ToString()} {DynamicObservationTypeAsString(s.dynamicObservationType)}")));

                Tracing.Logger.Log.IncrementalSchedulingArtifactChangeSample(loggingContext, samples);
            }
        }
    }
}
