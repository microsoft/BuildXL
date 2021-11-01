// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

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
    /// Extension methods for enums in the logging layer.
    /// </summary>
    public static class EnumToStringExtensions
    {
        /// <summary>
        /// Gets a string representation of <paramref name="kind"/> without allocating a string each time.
        /// </summary>
        public static string ToStringNoAlloc(this OperationKind kind)
            => kind switch
            {
                OperationKind.None => nameof(OperationKind.None),
                OperationKind.Startup => nameof(OperationKind.Startup),
                OperationKind.InProcessOperation => nameof(OperationKind.InProcessOperation),
                OperationKind.OutOfProcessOperation => nameof(OperationKind.OutOfProcessOperation),
                _ => kind.ToString()
            };

        /// <summary>
        /// Gets a string representation of <paramref name="status"/> without allocating a string each time.
        /// </summary>
        public static string ToStringNoAlloc(this OperationStatus status)
            => status switch
            {
                OperationStatus.Success => nameof(OperationStatus.Success),
                OperationStatus.Failure => nameof(OperationStatus.Failure),
                OperationStatus.CriticalException => nameof(OperationStatus.CriticalException),
                OperationStatus.Cancelled => nameof(OperationStatus.Cancelled),
                OperationStatus.CriticalFailure => nameof(OperationStatus.CriticalFailure),
                _ => status.ToString()
            };
    }

    /// <summary>
    /// Interface that contains the core data about the operation (for both starts and stops).
    /// </summary>
    public interface IOperationInfo
    {
        /// <summary>
        /// Message to log.
        /// </summary>
        string Message { get; }

        /// <summary>
        /// Name of an operation.
        /// </summary>
        string OperationName { get; }

        /// <summary>
        /// Name of a tracer (i.e. the origin of the message/operation).
        /// </summary>
        string TracerName { get; } // Component. WARNING: CloudBuild-sided change required to rename

        /// <nodoc />
        OperationKind OperationKind { get; }

        /// <summary>
        /// Tracing severity of the result.
        /// </summary>
        Severity Severity { get; }

        /// <summary>
        /// Id of an operation.
        /// </summary>
        string OperationId { get; }
    }

    /// <summary>
    /// Log message data.
    /// </summary>
    public readonly struct LogMessage : IOperationInfo
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
        public string TracerName { get; } // Component. WARNING: CloudBuild-sided change required to rename

        /// <nodoc />
        public OperationKind OperationKind { get; }

        /// <summary>
        /// Tracing severity of the result.
        /// </summary>
        public Severity Severity { get; }

        /// <summary>
        /// Id of an operation.
        /// </summary>
        public string OperationId { get; }

        /// <summary>
        /// An optional exception associated with the log message
        /// </summary>
        public Exception? Exception { get; }

        /// <nodoc />
        public LogMessage(
            string message,
            string operationName,
            string tracerName,
            OperationKind operationKind,
            string operationId,
            Severity severity)
        : this (message, operationName, tracerName, operationKind, operationId, severity, exception: null)
        {
        }

        /// <nodoc />
        public LogMessage(
            string message,
            string operationName,
            string tracerName,
            OperationKind operationKind,
            string operationId,
            Severity severity,
            Exception? exception)
        {
            Contract.RequiresNotNullOrEmpty(message, "message should not be null or empty");

            Message = message;
            OperationName = operationName;
            TracerName = tracerName;
            OperationKind = operationKind;
            Severity = severity;
            OperationId = operationId;
            Exception = exception;
        }
    }

    /// <summary>
    /// Contains information about the operation start.
    /// </summary>
    public readonly struct OperationStarted : IOperationInfo
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
        public string TracerName { get; } // Component. WARNING: CloudBuild-sided change required to rename

        /// <nodoc />
        public OperationKind OperationKind { get; }

        /// <summary>
        /// Tracing severity of the result.
        /// </summary>
        public Severity Severity { get; }

        /// <summary>
        /// Id of an operation.
        /// </summary>
        public string OperationId { get; }

        /// <nodoc />
        public OperationStarted(
            string message,
            string operationName,
            string tracerName,
            OperationKind operationKind,
            string operationId,
            Severity severity)
        {
            Contract.RequiresNotNullOrEmpty(message, "message should not be null or empty");
            Contract.RequiresNotNullOrEmpty(operationName, "operationName should not be null or empty");
            Contract.RequiresNotNullOrEmpty(tracerName, "tracerName should not be null or empty");

            Message = message;
            OperationName = operationName;
            TracerName = tracerName;
            OperationKind = operationKind;
            Severity = severity;
            OperationId = operationId;
        }
    }

    /// <summary>
    /// Represents an operation result used for tracing purposes.
    /// </summary>
    public readonly struct OperationResult : IOperationInfo
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
        public string TracerName { get; } // Component. WARNING: CloudBuild-sided change required to rename

        /// <summary>
        /// The result of an operation.
        /// </summary>
        public OperationStatus Status { get; }

        /// <nodoc />
        public OperationKind OperationKind { get; }

        /// <nodoc />
        public TimeSpan Duration { get; }

        /// <nodoc />
        public Exception? Exception { get; }

        /// <summary>
        /// Tracing severity of the result.
        /// </summary>
        public Severity Severity { get; }

        /// <summary>
        /// Id of an operation.
        /// </summary>
        public string OperationId { get; }

        /// <nodoc />
        public OperationResult(
            string message,
            string operationName,
            string tracerName,
            OperationStatus status,
            TimeSpan duration,
            OperationKind operationKind,
            Exception? exception,
            string operationId,
            Severity severity)
        {
            Contract.RequiresNotNullOrEmpty(message, "message should not be null or empty");
            Contract.RequiresNotNullOrEmpty(operationName, "operationName should not be null or empty");
            Contract.RequiresNotNullOrEmpty(tracerName, "tracerName should not be null or empty");

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
        public string TracerName { get; } // Component. WARNING: CloudBuild-sided change required to rename

        /// <nodoc />
        public Metric(string name, long value, string tracerName)
        {
            Contract.RequiresNotNullOrEmpty(name, "name should not be null or empty.");
            Contract.RequiresNotNullOrEmpty(tracerName, "tracerName should not be null or empty");

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
            Contract.RequiresNotNullOrEmpty(name, "name should not be null or empty.");

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
