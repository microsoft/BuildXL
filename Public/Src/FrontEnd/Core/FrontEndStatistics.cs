// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces;
using JetBrains.Annotations;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Interface for capturing statistics for different phases of the frontend.
    /// </summary>
    public interface IFrontEndStatistics : IWorkspaceStatistics
    {
        /// <nodoc/>
        void AnalysisCompleted(AbsolutePath path, TimeSpan duration);

        /// <nodoc/>
        TimeSpan GetOverallAnalysisDuration();

        /// <nodoc/>
        Counter SpecAstConversion { get; }

        /// <nodoc/>
        Counter SpecAstDeserialization { get; }

        /// <nodoc/>
        Counter SpecAstSerialization { get; }

        /// <nodoc/>
        Counter PublicFacadeComputation { get; }

        /// <summary>
        /// Counter with the number of reloads of source texts.
        /// </summary>
        CounterWithRootCause CounterWithRootCause { get; }

        /// <summary>
        /// Delegate that will be invoked for workspace computation progress reporting.
        /// </summary>
        [CanBeNull]
        EventHandler<WorkspaceProgressEventArgs> WorkspaceProgress { get; }

        /// <summary>
        /// Counter for all processed configuration files.
        /// </summary>
        Counter ConfigurationProcessing { get; }

        /// <summary>
        /// Counter to measure built-in prelude processing times.
        /// </summary>
        Counter PreludeProcessing { get; }

        /// <summary>
        /// Statistics for all nuget-related operations.
        /// </summary>
        INugetStatistics NugetStatistics { get; }

        /// <summary>
        /// Statistics for configuration processing.
        /// </summary>
        ILoadConfigStatistics LoadConfigStatistics { get; }
    }

    /// <summary>
    /// Captures statistic information about different stages of the DScript frontend pipeline.
    /// </summary>
    public sealed class FrontEndStatistics : WorkspaceStatistics, IFrontEndStatistics
    {
        private long m_analysisDurationInTicks;

        /// <nodoc />
        public FrontEndStatistics(EventHandler<WorkspaceProgressEventArgs> workspaceProgressHandler = null)
        {
            WorkspaceProgress = workspaceProgressHandler;
        }

        /// <inheritdoc />
        public Counter SpecAstConversion { get; } = new Counter();

        /// <inheritdoc />
        public Counter SpecAstDeserialization { get; } = new Counter();

        /// <inheritdoc />
        public Counter SpecAstSerialization { get; } = new Counter();

        /// <inheritdoc />
        public Counter PublicFacadeComputation { get; } = new Counter();

        /// <inheritdoc />
        public CounterWithRootCause CounterWithRootCause { get; } = new CounterWithRootCause();

        /// <inheritdoc />
        public Counter ConfigurationProcessing { get; } = new Counter();

        /// <inheritdoc />
        public Counter PreludeProcessing { get; } = new Counter();

        /// <inheritdoc />
        void IFrontEndStatistics.AnalysisCompleted(AbsolutePath path, TimeSpan duration)
        {
            Interlocked.Add(ref m_analysisDurationInTicks, duration.Ticks);
        }

        /// <inheritdoc />
        TimeSpan IFrontEndStatistics.GetOverallAnalysisDuration()
        {
            return TimeSpan.FromTicks(Interlocked.Read(ref m_analysisDurationInTicks));
        }

        /// <inheritdoc />
        public EventHandler<WorkspaceProgressEventArgs> WorkspaceProgress { get; }

        /// <inheritdoc />
        public INugetStatistics NugetStatistics { get; } = new NugetStatistics();

        /// <inheritdoc />
        public ILoadConfigStatistics LoadConfigStatistics { get; } = new LoadConfigStatistics();
    }
}
