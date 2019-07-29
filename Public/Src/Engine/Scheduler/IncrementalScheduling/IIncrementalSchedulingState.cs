// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Scheduler.Graph;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler.IncrementalScheduling
{
    /// <summary>
    /// Interface for incremental scheduling state.
    /// </summary>
    /// <remarks>
    /// This interface is needed, possibly temporarily, so that graph-specific and graph-agnostic
    /// can co-exist side-by-side in the codebase (not during runtime).
    /// </remarks>
    public interface IIncrementalSchedulingState : IFileChangeTrackingObserver
    {
        /// <summary>
        /// Dirty node tracker.
        /// </summary>
        DirtyNodeTracker DirtyNodeTracker { get; }

        /// <summary>
        /// Pending updates to node tracker.
        /// </summary>
        DirtyNodeTracker.PendingUpdatedState PendingUpdates { get; }

        /// <summary>
        /// The current pip graph.
        /// </summary>
        PipGraph PipGraph { get; }

        /// <summary>
        /// Saves incremental scheduling state if the state has changed.
        /// </summary>
        /// <param name="atomicSaveToken">Save token obtained from <see cref="FileChangeTracker"/>.</param>
        /// <param name="incrementalSchedulingStatePath">File to which this state will be saved.</param>
        /// <returns>True if state is saved; otherwise false.</returns>
        bool SaveIfChanged(FileEnvelopeId atomicSaveToken, string incrementalSchedulingStatePath);

        /// <summary>
        /// Records dynamic observations during execution phase.
        /// </summary>
        /// <param name="nodeId">Node id that correponds to a pip.</param>
        /// <param name="dynamicallyObservedFilePaths">Dynamically observed files.</param>
        /// <param name="dynamicallyObservedEnumerationPaths">Dynamically observed enumerations.</param>
        /// <param name="dynamicDirectoryContents">Dynamic directory contents.</param>
        void RecordDynamicObservations(
            NodeId nodeId,
            IEnumerable<string> dynamicallyObservedFilePaths,
            IEnumerable<string> dynamicallyObservedEnumerationPaths,
            IEnumerable<(string directory, IEnumerable<string> fileArtifactsCollection)> dynamicDirectoryContents);

        /// <summary>
        /// Writes textual format of the instance of <see cref="IIncrementalSchedulingState"/>.
        /// </summary>
        /// <param name="writer">A text writer.</param>
        void WriteText(TextWriter writer);

        /// <summary>
        /// Checks if this instance of <see cref="IIncrementalSchedulingState"/> is reusable with respect to the given pip graph, configuration, and preserved outputs salt. If it is reusable, returns a reusable close of it, otherwise returns null.
        /// </summary>
        /// <param name="loggingContext">New logging context.</param>
        /// <param name="pipGraph">New pip graph.</param>
        /// <param name="configuration">New configuration.</param>
        /// <param name="preserveOutputSalt">New preserved outputs salt.</param>
        /// <param name="tempDirectoryCleaner">Temporary directory cleaner.</param>
        /// <returns>A new reusable instance of <see cref="IIncrementalSchedulingState"/> if this instance is reusable; otherwise null.</returns>
        /// <remarks>
        /// A logging context needs to be given because the logging context maintained by this instance of <see cref="IIncrementalSchedulingState"/> can be
        /// associated with some previous build.
        /// </remarks>
        IIncrementalSchedulingState Reuse(LoggingContext loggingContext, PipGraph pipGraph, IConfiguration configuration, ContentHash preserveOutputSalt, ITempCleaner tempDirectoryCleaner);
    }
}
