// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler.IncrementalScheduling
{
    /// <summary>
    /// Factory for <see cref="IIncrementalSchedulingState"/>.
    /// </summary>
    public class IncrementalSchedulingStateFactory
    {
        private readonly LoggingContext m_loggingContext;

        private readonly ITempCleaner m_tempDirectoryCleaner;

        private readonly bool m_analysisMode;

        /// <summary>
        /// Creates an instance of <see cref="IncrementalSchedulingStateFactory"/>.
        /// </summary>
        public IncrementalSchedulingStateFactory(
            LoggingContext loggingContext,
            bool analysisMode = false,            
            ITempCleaner tempDirectoryCleaner = null)
        {
            Contract.Requires(loggingContext != null);

            m_loggingContext = loggingContext;
            m_analysisMode = analysisMode;
            m_tempDirectoryCleaner = tempDirectoryCleaner;
        }

        /// <summary>
        /// Creates a new instance of <see cref="IIncrementalSchedulingState"/>.
        /// </summary>
        public IIncrementalSchedulingState CreateNew(
            FileEnvelopeId atomicSaveToken,
            PipGraph pipGraph,
            IConfiguration configuration,
            ContentHash preserveOutputSalt)
        {
            Contract.Requires(atomicSaveToken.IsValid);
            Contract.Requires(pipGraph != null);
            Contract.Requires(configuration != null);

            return GraphAgnosticIncrementalSchedulingState.CreateNew(
                    m_loggingContext,
                    atomicSaveToken,
                    pipGraph,
                    configuration,
                    preserveOutputSalt,
                    m_tempDirectoryCleaner);
        }

        /// <summary>
        /// Loads an existing instance of <see cref="IIncrementalSchedulingState"/> from a given file or reuse it from a given <see cref="SchedulerState"/>.
        /// </summary>
        public IIncrementalSchedulingState LoadOrReuse(
            FileEnvelopeId atomicSaveToken,
            PipGraph pipGraph,
            IConfiguration configuration,
            ContentHash preserveOutputSalt,
            string incrementalSchedulingStatePath,
            SchedulerState schedulerState)
        {
            Contract.Requires(atomicSaveToken.IsValid);
            return LoadOrReuseInternal(atomicSaveToken, pipGraph, configuration, preserveOutputSalt, incrementalSchedulingStatePath, schedulerState);
        }

        /// <summary>
        /// Same behavior as <see cref="LoadOrReuse"/>, but the file envelopeId is ignored
        /// </summary>
        /// <remarks>
        /// Only available when the factory is constructed with analysis mode on
        /// </remarks>
        public IIncrementalSchedulingState LoadOrReuseIgnoringFileEnvelope(
            PipGraph pipGraph,
            IConfiguration configuration,
            ContentHash preserveOutputSalt,
            string incrementalSchedulingStatePath,
            SchedulerState schedulerState)
        {
            Contract.Assert(m_analysisMode);
            return LoadOrReuseInternal(FileEnvelopeId.Invalid, pipGraph, configuration, preserveOutputSalt, incrementalSchedulingStatePath, schedulerState);
        }


        private IIncrementalSchedulingState LoadOrReuseInternal(
            FileEnvelopeId atomicSaveToken,
            PipGraph pipGraph,
            IConfiguration configuration,
            ContentHash preserveOutputSalt,
            string incrementalSchedulingStatePath,
            SchedulerState schedulerState)
        {
            Contract.Requires(pipGraph != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(incrementalSchedulingStatePath));
            Contract.Assert(m_analysisMode || configuration != null);

            if (!m_analysisMode && schedulerState != null && schedulerState.IncrementalSchedulingState != null)
            {
                IIncrementalSchedulingState reusedState = schedulerState.IncrementalSchedulingState.Reuse(m_loggingContext, pipGraph, configuration, preserveOutputSalt, m_tempDirectoryCleaner);

                if (reusedState != null)
                {
                    return reusedState;
                }
            }

            return GraphAgnosticIncrementalSchedulingState.Load(
                    m_loggingContext,
                    atomicSaveToken,
                    pipGraph,
                    configuration,
                    preserveOutputSalt,
                    incrementalSchedulingStatePath,
                    analysisModeOnly: m_analysisMode,
                    tempDirectoryCleaner: m_tempDirectoryCleaner);
        }
    }
}
