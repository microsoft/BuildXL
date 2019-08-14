// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!
#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Scheduler.Tracing
{
    // This file provides support for a build's execution log in general (the mechanics of having an execution log, but not the particular events).

    /// <summary>
    /// Basic <see cref="IExecutionLogTarget"/> which provides default, override-able implementations for each event.
    /// The default implementation for each event forwards to <see cref="OnUnhandledEvent"/>.
    /// Most event consumers should inherit from this class, unless it is important that they handle all events (note that new events may be added over time).
    /// </summary>
    public abstract class ExecutionLogTargetBase : IExecutionLogTarget
    {
        private readonly bool[] m_disabledEvents = new bool[EnumTraits<ExecutionEventId>.MaxValue + 1];

        /// <summary>
        /// The worker id of the current event processing. For use during event handlers.
        /// </summary>
        protected uint CurrentEventWorkerId { get; private set; }

        /// <summary>
        /// Gets a value indicating if the execution log target can process worker events
        /// </summary>
        public virtual bool CanHandleWorkerEvents { get; set; }

        /// <inheritdoc />
        public virtual IExecutionLogTarget CreateWorkerTarget(uint workerId)
        {
            return null;
        }

        /// <inheritdoc/>
        public virtual void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        {
            ReportUnhandledEvent(data);
        }

        /// <inheritdoc />
        public virtual void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            ReportUnhandledEvent(data);
        }

        /// <inheritdoc />
        public virtual void WorkerList(WorkerListEventData data)
        {
            ReportUnhandledEvent(data);
        }

        /// <inheritdoc />
        public virtual void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            ReportUnhandledEvent(data);
        }

        /// <inheritdoc />
        public virtual void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
        {
            ReportUnhandledEvent(data);
        }

        /// <summary>
        ///  Observed inputs data is discovered.
        /// </summary>
        public virtual void ObservedInputs(ObservedInputsEventData data)
        {
            OnUnhandledEvent(ExecutionEventId.ObservedInputs);
        }

        /// <summary>
        /// Handler for <see cref="IExecutionLogTarget.ProcessFingerprintComputation"/> event
        /// </summary>
        public virtual void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            ReportUnhandledEvent(data);
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        void IExecutionLogTarget.ProcessFingerprintComputation(ProcessFingerprintComputationEventData data)
        {
            // Call old observed inputs event
            if (CanHandleEvent(ExecutionEventId.ObservedInputs, 0, 0))
            {
                foreach (var strongFingerprintComputation in data.StrongFingerprintComputations)
                {
                    if (strongFingerprintComputation.Succeeded)
                    {
                        ObservedInputs(new ObservedInputsEventData()
                        {
                            PipId = data.PipId,
                            ObservedInputs = strongFingerprintComputation.ObservedInputs,
                        });
                    }
                }
            }

            ProcessFingerprintComputed(data);
        }

        /// <inheritdoc />
        public virtual void ExtraEventDataReported(ExtraEventData data)
        {
            ReportUnhandledEvent(data);
        }

        /// <inheritdoc />
        public virtual void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            ReportUnhandledEvent(data);
        }

        /// <inheritdoc />
        public virtual void DependencyViolationReported(DependencyViolationEventData data)
        {
            ReportUnhandledEvent(data);
        }

        /// <summary>
        /// PipExecutionStep performance is reported.
        /// </summary>
        public virtual void PipExecutionStepPerformanceReported(PipExecutionStepPerformanceEventData data)
        {
            ReportUnhandledEvent(data);
        }

        /// <summary>
        /// Pip cache miss
        /// </summary>
        public virtual void PipCacheMiss(PipCacheMissEventData data)
        {
            ReportUnhandledEvent(data);
        }

        /// <summary>
        /// Resource and PipQueue usage is reported
        /// </summary>
        public virtual void StatusReported(StatusEventData data)
        {
            ReportUnhandledEvent(data);
        }

        /// <summary>
        /// Single event giving build invocation information that contains configuration details useful for analyzers.
        /// </summary>
        public virtual void DominoInvocation(DominoInvocationEventData data)
        {
            ReportUnhandledEvent(data);
        }

        protected virtual void ReportUnhandledEvent<TEventData>(TEventData data)
            where TEventData : struct, IExecutionLogEventData<TEventData>
        {
            OnUnhandledEvent(data.Metadata.EventId);
        }

        /// <summary>
        /// Disables unhanded events. Override this method or <see cref="CanHandleEvent(ExecutionEventId, uint, long, int)"/> if this behavior is not desired
        /// </summary>
        protected virtual void OnUnhandledEvent(ExecutionEventId eventId)
        {
            // Disable the event if it is unhandled so that
            // subsequent calls will not deserialize the event data
            DisableEvent(eventId);
        }

        /// <summary>
        /// Disables an event.
        /// </summary>
        protected void DisableEvent(ExecutionEventId eventId)
        {
            m_disabledEvents[(int)eventId] = true;
        }

        /// <inheritdoc />
        public virtual void Dispose()
        {
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        bool IExecutionLogTarget.CanHandleEvent(ExecutionEventId eventId, uint workerId, long timestamp, int eventPayloadSize)
        {
            CurrentEventWorkerId = workerId;

            if (eventId == ExecutionEventId.ProcessFingerprintComputation &&
                CanHandleEvent(ExecutionEventId.ObservedInputs, workerId, timestamp, eventPayloadSize))
            {
                return true;
            }

            return CanHandleEvent(eventId, workerId, timestamp, eventPayloadSize);
        }

        /// <nodoc />
        public virtual bool CanHandleEvent(ExecutionEventId eventId, uint workerId, long timestamp, int eventPayloadSize)
        {
            if (!CanHandleWorkerEvents && workerId != 0)
            {
                // By default execution log targets cannot handle events from workers other than the local worker
                return false;
            }

            return CanHandleEvent(eventId, timestamp, eventPayloadSize);
        }

        /// <nodoc />
        public virtual bool CanHandleEvent(ExecutionEventId eventId, long timestamp, int eventPayloadSize)
        {
            return !m_disabledEvents[(int)eventId];
        }
    }

    /// <summary>
    /// Per-event metadata as provided on the static <see cref="ExecutionLogMetadata"/>.
    /// This establishes a single (<see cref="IExecutionLogTarget"/> method, event data, event ID) triples.
    /// </summary>
    /// <remarks>
    /// All instances are of the type <see cref="ExecutionLogEventMetadata{TEventData}"/>, which adds the event-data part of the triple
    /// via its type parameter. This abstract base facilitates erasure of the event-data type, which allows for e.g. generic deserialization
    /// (by dispatching to <see cref="DeserializeAndLogToTarget"/> based on event ID).
    /// </remarks>
    public abstract class ExecutionLogEventMetadata
    {
        /// <summary>
        /// Event ID corresponding to a log-target method and event data type.
        /// </summary>
        public readonly ExecutionEventId EventId;

        /// <nodoc />
        protected ExecutionLogEventMetadata(ExecutionEventId eventId)
        {
            EventId = eventId;
        }

        /// <summary>
        /// Translates a serialized stream containing an event (of this type) into an invocation on an <see cref="IExecutionLogTarget"/>.
        /// </summary>
        /// <remarks>
        /// This method hides the event-data type and so is suitable for generic deserialization.
        /// </remarks>
        public abstract void DeserializeAndLogToTarget(BinaryLogReader.EventReader eventReader, IExecutionLogTarget target);
    }

    /// <summary>
    /// See <see cref="ExecutionLogEventMetadata"/>.
    /// </summary>
    public sealed class ExecutionLogEventMetadata<TEventData> : ExecutionLogEventMetadata
        where TEventData : struct, IExecutionLogEventData<TEventData>
    {
        private readonly Action<TEventData, IExecutionLogTarget> m_process;

        /// <nodoc />
        public ExecutionLogEventMetadata(ExecutionEventId eventId, Action<TEventData, IExecutionLogTarget> logToTarget)
            : base(eventId)
        {
            m_process = logToTarget;
        }

        /// <inheritdoc />
        public override void DeserializeAndLogToTarget(BinaryLogReader.EventReader eventReader, IExecutionLogTarget target)
        {
            TEventData data = default(TEventData);
            data.DeserializeAndUpdate(eventReader);
            m_process(data, target);
        }

        /// <nodoc />
        public void LogToTarget(TEventData data, IExecutionLogTarget target)
        {
            m_process(data, target);
        }
    }

    /// <summary>
    /// Interface for event-specific data.
    /// </summary>
    /// <remarks>
    /// Each event has a unique event-data type. One can associate event-data to its owning event via <see cref="Metadata"/>,
    /// which should be a member of <see cref="ExecutionLogMetadata.Events"/>.
    /// To facilitate persistence of events in a replay-able file log, event-data is round-trip serializable.
    /// Deserialization is performed in-place (since it must be an instance operation to be part of the interface). Note that
    /// in-place deserialization is called on a <c>default(...)</c> instance, so deserialization may not assume constructor invariants.
    /// </remarks>
    public interface IExecutionLogEventData<TSelf>
        where TSelf : struct, IExecutionLogEventData<TSelf>
    {
        /// <summary>
        /// Corresponding metadata on <see cref="ExecutionLogMetadata.Events"/>.
        /// </summary>
        ExecutionLogEventMetadata<TSelf> Metadata { get; }

        /// <nodoc />
        void Serialize(BinaryLogger.EventWriter writer);

        /// <nodoc />
        void DeserializeAndUpdate(BinaryLogReader.EventReader reader);
    }

    /// <summary>
    /// Log target which persists execution events to a <see cref="BinaryLogger"/>. The event log may be replayed (to a consuming <see cref="IExecutionLogTarget"/>)
    /// via <see cref="ExecutionLogFileReader"/>.
    /// </summary>
    public class ExecutionLogFileTarget : ExecutionLogTargetBase
    {
        /// <summary>
        /// Gets the log id of the execution log
        /// </summary>
        public Guid LogId => m_logFile.LogId;

        private readonly bool m_closeLogFileOnDispose;
        private readonly BinaryLogger m_logFile;
        private readonly uint m_workerId = 0;
        private readonly IReadOnlyList<int> m_disabledEventIds;

        /// <summary>
        /// Performance counters.
        /// </summary>
        public CounterCollection<ExecutionLogCounters> Counters { get; } = new CounterCollection<ExecutionLogCounters>();

        /// <nodoc />
        public ExecutionLogFileTarget(BinaryLogger logFile, bool closeLogFileOnDispose = true, IReadOnlyList<int> disabledEventIds = null)
        {
            Contract.Requires(logFile != null);
            m_closeLogFileOnDispose = closeLogFileOnDispose;
            m_logFile = logFile;
            m_disabledEventIds = disabledEventIds;

            // Disable the specified event ids (if any)
            if (m_disabledEventIds != null)
            {
                foreach (var eventId in m_disabledEventIds)
                {
                    if ((ulong)eventId <= EnumTraits<ExecutionEventId>.MaxValue)
                    {
                        DisableEvent((ExecutionEventId)eventId);
                    }
                }
            }
        }

        /// <summary>
        /// Clones the execution log target with the same backing binary logger but against a different worker id
        /// </summary>
        private ExecutionLogFileTarget(BinaryLogger logFile, uint workerId, IReadOnlyList<int> disabledEventIds)
            : this(logFile, closeLogFileOnDispose: false, disabledEventIds: disabledEventIds)
        {
            m_workerId = workerId;
        }

        private void Log<TEventData>(TEventData data) where TEventData : struct, IExecutionLogEventData<TEventData>
        {
            using (BinaryLogger.EventScope eventScope = m_logFile.StartEvent((uint)data.Metadata.EventId, m_workerId))
            {
                data.Serialize(eventScope.Writer);
            }
        }

        /// <summary>
        /// Clones the execution log target with the same backing binary logger but against a different worker id
        /// </summary>
        public override IExecutionLogTarget CreateWorkerTarget(uint workerId)
        {
            return new ExecutionLogFileTarget(m_logFile, workerId, disabledEventIds: m_disabledEventIds);
        }

        protected override void ReportUnhandledEvent<TEventData>(TEventData data)
        {
            using (Counters.StartStopwatch(ExecutionLogCounters.ExecutionLogFileLoggingTime))
            {
                if (CanHandleEvent(data.Metadata.EventId, 0, 0))
                {
                    Log(data);
                }
            }
        }

        public override bool CanHandleEvent(ExecutionEventId eventId, long timestamp, int eventPayloadSize)
        {
            // Disable observed inputs which are logged by ProcessFingerprintComputationEvent (that data
            // would be redundant. It will be reported on deserialization from ProcessFingerprintComputationEvent as
            // well.)
            return (eventId != ExecutionEventId.ObservedInputs) && base.CanHandleEvent(eventId, timestamp, eventPayloadSize);
        }

        protected override void OnUnhandledEvent(ExecutionEventId eventId)
        {
            Contract.Assert(false, I($"Execution log should log all event types. Unhandled event: {eventId}"));
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            if (m_closeLogFileOnDispose)
            {
                m_logFile.Dispose();
            }
        }
    }

    /// <summary>
    /// Replays events (as persisted by <see cref="ExecutionLogFileTarget"/>) to an <see cref="IExecutionLogTarget"/>.
    /// The caller is responsible for disposing the log target, but the underlying stream may optionally be owned by the reader.
    /// </summary>
    public class ExecutionLogFileReader : IDisposable
    {
        /// <summary>
        /// Gets the log id of the execution log
        /// </summary>
        public Guid? LogId => Reader.LogId;

        private readonly IExecutionLogTarget m_target;
        protected readonly BinaryLogReader Reader;

        /// Constructor
        public ExecutionLogFileReader(BinaryLogReader binaryLogReader, IExecutionLogTarget target)
        {
            Contract.Requires(binaryLogReader != null);
            Contract.Requires(target != null);

            m_target = target;
            Reader = binaryLogReader;
            RegisterHandlers(Reader);
        }

        /// <nodoc />
        public ExecutionLogFileReader(Stream logStream, PipExecutionContext context, IExecutionLogTarget target, bool closeStreamOnDispose = true)
            : this(new BinaryLogReader(logStream, context, closeStreamOnDispose: closeStreamOnDispose), target)
        {
        }

        private void RegisterHandlers(BinaryLogReader logReader)
        {
            foreach (ExecutionLogEventMetadata meta in ExecutionLogMetadata.Events)
            {
                ExecutionLogEventMetadata thisMeta = meta;
                logReader.RegisterHandler(
                    (uint)thisMeta.EventId,
                    (id, workerId, timestamp, eventReader) =>
                    {
                        if (m_target.CanHandleEvent(thisMeta.EventId, workerId, timestamp, eventReader.CurrentEventPayloadSize))
                        {
                            thisMeta.DeserializeAndLogToTarget(eventReader, m_target);
                        }
                    });
            }
        }

        /// <summary>
        /// Attempts to read a single event and dispatch it to the target.
        /// </summary>
        public BinaryLogReader.EventReadResult ReadNextEvent()
        {
            return Reader.ReadEvent();
        }

        /// <summary>
        /// Attempts to read every event, dispatching each to the target.
        /// Returns 'false' in the event of a condition such as <see cref="BinaryLogReader.EventReadResult.UnexpectedEndOfStream"/>.
        /// </summary>
        public bool ReadAllEvents()
        {
            BinaryLogReader.EventReadResult result;
            do
            {
                result = ReadNextEvent();
            }
            while (result == BinaryLogReader.EventReadResult.Success);

            return result == BinaryLogReader.EventReadResult.EndOfStream;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Reader.Dispose();
        }
    }
}
