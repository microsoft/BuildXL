// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;

namespace BuildXL.Cache.ContentStore.Interfaces.Logging
{
    /// <summary>
    /// Status of an operation.
    /// </summary>
    public enum OperationStatus
    {
        /// <summary>
        /// Operation finished successfully.
        /// </summary>
        Success,

        /// <summary>
        /// Operation failed with (potentially) recoverable error.
        /// </summary>
        Failure,

        /// <summary>
        /// Operation failed with unrecoverable error that (most likely) indicates issues with the code.
        /// </summary>
        CriticalException,

        /// <summary>
        /// The operation was cancelled.
        /// </summary>
        Cancelled,

        /// <summary>
        /// Critical operation failed that can cause severe service issues (like creating/restoring checkpoints for local location store) or an initialization of a top-level component.
        /// </summary>
        CriticalFailure,
    }

    /// <summary>
    /// Kinds of an operation.
    /// </summary>
    public enum OperationKind
    {
        /// <summary>
        /// The kind is not specified.
        /// </summary>
        None,

        /// <summary>
        /// Operation is an initialization operation.
        /// </summary>
        Startup,

        /// <summary>
        /// Operation is a regular in-process or single machine operation.
        /// </summary>
        InProcessOperation,

        /// <summary>
        /// Operation is out-of-proc operation that interacts with an external system.
        /// </summary>
        OutOfProcessOperation,
    }

    /// <summary>
    /// Represents an operation result used for tracing purposes.
    /// </summary>
    public readonly struct OperationResult
    {
        /// <summary>
        /// Message to log.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Name of an operation.
        /// </summary>
        public string OperationName { get; }

        /// <summary>
        /// Name of a tracer (i.e. the origin of the message/operation).
        /// </summary>
        public string TracerName { get; } // Component

        /// <summary>
        /// The result of an operation.
        /// </summary>
        public OperationStatus Status { get; }

        /// <nodoc />
        public OperationKind OperationKind { get; }

        /// <nodoc />
        public TimeSpan Duration { get; }

        /// <nodoc />
        public Exception Exception { get; }

        /// <summary>
        /// Tracing severity of the result.
        /// </summary>
        public Severity Severity { get; }

        /// <summary>
        /// Id of an operation.
        /// </summary>
        public Guid OperationId { get; }

        /// <nodoc />
        public OperationResult(
            string message,
            string operationName,
            string tracerName,
            OperationStatus status,
            TimeSpan duration,
            OperationKind operationKind,
            Exception exception,
            Guid operationId,
            Severity severity)
        {
            Contract.Requires(!string.IsNullOrEmpty(message), "message should not be null or empty");
            Contract.Requires(!string.IsNullOrEmpty(operationName), "operationName should not be null or empty");
            Contract.Requires(!string.IsNullOrEmpty(tracerName), "tracerName should not be null or empty");

            Message = message;
            OperationName = operationName;
            TracerName = tracerName;
            Status = status;
            Duration = duration;
            OperationKind = operationKind;
            Exception = exception;
            Severity = severity;
            OperationId = operationId;
        }

        /// <summary>
        /// Deconstruction method of the operation result.
        /// </summary>
        public void Deconstruct(
            out string message,
            out string operationName,
            out string tracerName,
            out OperationStatus status,
            out TimeSpan duration,
            out OperationKind operationKind)
        {
            message = Message;
            operationName = OperationName;
            tracerName = TracerName;
            status = Status;
            duration = Duration;
            operationKind = OperationKind;
        }
    }

    /// <nodoc />
    public readonly struct Metric
    {
        /// <summary>
        /// Name of the metric. Should contain the unit in which the metric is measured.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The value related to the metric.
        /// </summary>
        public long Value { get; }

        /// <summary>
        /// Name of a tracer (i.e. the origin of the message/operation).
        /// </summary>
        public string TracerName { get; }

        /// <nodoc />
        public Metric(string name, long value, string tracerName)
        {
            Contract.Requires(!string.IsNullOrEmpty(name), "name should not be null or empty.");
            Contract.Requires(!string.IsNullOrEmpty(tracerName), "tracerName should not be null or empty");

            Name = name;
            Value = value;
            TracerName = tracerName;
        }

        /// <summary>
        /// Decontruction method for a metric.
        /// </summary>
        public void Deconstruct(
            out string name,
            out long value,
            out string tracerName)
        {
            name = Name;
            value = Value;
            tracerName = TracerName;
        }
    }

    /// <summary>
    /// A statistic that will be logged as a top-level metric
    /// </summary>
    public readonly struct Statistic
    {
        /// <summary>
        /// The name of the statistic
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The value of the statistic
        /// </summary>
        public long Value { get; }

        /// <nodoc />
        public Statistic(string name, long value)
        {
            Contract.Requires(!string.IsNullOrEmpty(name), "name should not be null or empty.");

            Name = name;
            Value = value;
        }

        /// <summary>
        /// Decontruction method for a statistic.
        /// </summary>
        public void Deconstruct(
            out string name,
            out long value)
        {
            name = Name;
            value = Value;
        }
    }

    /// <summary>
    /// A special logger interface with additional operation for tracking operations.
    /// </summary>
    public interface IOperationLogger : ILogger
    {
        /// <summary>
        /// Tracks that an operation has finished.
        /// </summary>
        void OperationFinished(in OperationResult result);

        /// <summary>
        /// Tracks the value of a specific metric
        /// </summary>
        void TrackMetric(in Metric metric);

        /// <summary>
        /// Tracks the value of a statistic as a top-level metric
        /// </summary>
        void TrackTopLevelStatistic(in Statistic statistic);

        /// <summary>
        /// Registers build ID to track with each log message
        /// </summary>
        void RegisterBuildId(string buildId);

        /// <summary>
        /// Unregisters build ID once build completes
        /// </summary>
        void UnregisterBuildId();
    }
}
