// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// Replays events (as persisted by <see cref="ExecutionLogFileTarget"/>) to an <see cref="IExecutionLogTarget"/>.
    /// This reader class uses boolean flags to set what events to replay. When it encounters a disabled event, it will not deserialize it, it will skip over it instead.
    /// </summary>
    public sealed class FilteredExecutionLogFileReader : IDisposable
    {
        /// <summary>
        /// Gets the log id of the execution log
        /// </summary>
        public Guid? LogId
        {
            get
            {
                return m_reader.LogId;
            }
        }

        /// <summary>
        /// Binary log reader used to deserialize events
        /// </summary>
        private readonly ParallelExecutionLogFileReader m_reader;

        /// <summary>
        /// Flag that signal that there is at least one handler available.
        /// </summary>
        private readonly bool m_hasAtLeastOneHandler;

        /// <summary>
        /// Internal constructor
        /// </summary>
        /// <param name="logStream"> Specifies the stream to read from</param>
        /// <param name="context">Pip execution context</param>
        /// <param name="target">IExecutionLogTarget that implements the required event handlers</param>
        /// <param name="closeStreamOnDispose">Flag that tells the binary reader to close the stream when it is disposed</param>
        /// <param name="loadOptions">Specifies what to load from the execution log</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Target = "filteredTarget")]
        internal FilteredExecutionLogFileReader(
            string logFilename,
            PipExecutionContext context,
            IExecutionLogTarget target,
            ExecutionLogLoadOptions loadOptions = ExecutionLogLoadOptions.LoadPipDataBuildGraphAndPipPerformanceData)
        {
            Contract.Requires(logFilename != null);
            Contract.Requires(context != null);
            Contract.Requires(target != null);

            // Wrap execution log target with filtering log target which will override CanHandleEvent
            // so that events are not deserialized or reported from reader
            var filteredTarget = new FilteringExecutionLogTarget(target, loadOptions);
            m_reader = new ParallelExecutionLogFileReader(logFilename, context, filteredTarget);

            // signal that we have at least one handler available
            m_hasAtLeastOneHandler = (loadOptions & (ExecutionLogLoadOptions.LoadPipExecutionPerformanceData |
                                                        ExecutionLogLoadOptions.LoadFileHashValues |
                                                        ExecutionLogLoadOptions.LoadObservedInputs |
                                                        ExecutionLogLoadOptions.LoadProcessMonitoringData |
                                                        ExecutionLogLoadOptions.LoadDirectoryMemberships)) != 0;
        }

        /// <summary>
        /// Log target which overrides <see cref="CanHandleEvent"/> return value based on load options
        /// </summary>
        private sealed class FilteringExecutionLogTarget : ExecutionLogTargetBase
        {
            private readonly IExecutionLogTarget m_target;
            private readonly ExecutionLogLoadOptions m_loadOptions;

            public FilteringExecutionLogTarget(IExecutionLogTarget target, ExecutionLogLoadOptions loadOptions)
            {
                m_target = target;
                m_loadOptions = loadOptions;
            }

            protected override void ReportUnhandledEvent<TEventData>(TEventData data)
            {
                data.Metadata.LogToTarget(data, m_target);
            }

            public override bool CanHandleEvent(ExecutionEventId eventId, long timestamp, int eventPayloadSize)
            {
                ExecutionLogLoadOptions? eventLoadOption = GetLoadOption(eventId);
                if (!eventLoadOption.HasValue)
                {
                    return false;
                }

                // When DoNotLoadSourceFiles is set and LoadDirectoryMemberships is not set
                // we do not have to process ObservedInputs events
                if (((m_loadOptions & ExecutionLogLoadOptions.DoNotLoadSourceFiles) != 0) &&
                    ((m_loadOptions & ExecutionLogLoadOptions.LoadDirectoryMemberships) == 0) &&
                    (eventLoadOption == ExecutionLogLoadOptions.LoadObservedInputs))
                {
                    return false;
                }

                if (eventLoadOption.Value == ExecutionLogLoadOptions.None /* None load option (i.e. always supported) */
                    || (eventLoadOption.Value & m_loadOptions) != 0)
                {
                    return true;
                }

                return false;
            }

            private static ExecutionLogLoadOptions? GetLoadOption(ExecutionEventId eventId)
            {
                switch (eventId)
                {
                    case ExecutionEventId.FileArtifactContentDecided:
                        return ExecutionLogLoadOptions.LoadFileHashValues;

                    case ExecutionEventId.PipExecutionPerformance:
                        return ExecutionLogLoadOptions.LoadPipExecutionPerformanceData;

                    case ExecutionEventId.DirectoryMembershipHashed:
                        return ExecutionLogLoadOptions.LoadDirectoryMemberships;

                    case ExecutionEventId.ProcessFingerprintComputation:
                        return ExecutionLogLoadOptions.LoadObservedInputs | ExecutionLogLoadOptions.LoadProcessFingerprintComputations;

                    case ExecutionEventId.ProcessExecutionMonitoringReported:
                        return ExecutionLogLoadOptions.LoadProcessMonitoringData;

                    case ExecutionEventId.BuildSessionConfiguration:
                        // Extra event data is always supported
                        return ExecutionLogLoadOptions.None;
                    default:
                        return null;
                }
            }

            public override void Dispose()
            {
                m_target.Dispose();
            }
        }

        /// <summary>
        /// Attempts to read every event, dispatching each to the target.
        /// Returns 'false' in the event of a condition such as <see cref="BinaryLogReader.EventReadResult.UnexpectedEndOfStream"/>.
        /// </summary>
        public bool ReadAllEvents()
        {
            // do nothing when there are no handlers available
            if (!m_hasAtLeastOneHandler)
            {
                return true;
            }

            return m_reader.ReadAllEventsInParallel();
        }

        /// <summary>
        /// IDisposable implementation
        /// </summary>
        public void Dispose()
        {
            m_reader.Dispose();
        }
    }
}
