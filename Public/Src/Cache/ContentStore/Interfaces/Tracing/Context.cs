// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;

#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.Tracing
{
    /// <summary>
    /// A tracing context for a call tree where all tracing is related.
    /// </summary>
    public class Context
    {
        /// <summary>
        ///     Cached string representation of <see cref="Id"/> property for performance reasons
        /// </summary>
        private readonly string _idAsString;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Context"/> class.
        /// </summary>
        /// <param name="logger">
        ///     Caller's logger to be invoked as processing occurs.
        /// </param>
        public Context(ILogger logger)
            : this(Guid.NewGuid(), logger)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Context"/> class.
        /// </summary>
        public Context(Guid id, ILogger logger)
        {
            Id = id;
            Logger = logger;
            _idAsString = id.ToString();
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Context"/> class.
        /// </summary>
        public Context(Context other, [CallerMemberName]string? caller = null)
            : this(other, Guid.NewGuid(), caller)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Context"/> class.
        /// </summary>
        public Context(Context other, Guid id, [CallerMemberName]string? caller = null)
            : this(id, other.Logger)
        {
            Debug($"{caller}: {other._idAsString} parent to {_idAsString}");
        }

        /// <nodoc />
        public Context CreateNested([CallerMemberName]string? caller = null)
        {
            return new Context(this, caller);
        }

        /// <nodoc />
        public Context CreateNested(Guid id, [CallerMemberName]string? caller = null)
        {
            return new Context(this, id, caller);
        }

        /// <summary>
        ///     Gets the unique Id.
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        ///     Gets the associated tracing logger.
        /// </summary>
        public ILogger Logger { get; }

        /// <summary>
        ///     Gets a value indicating whether tracing for this context is enabled.
        /// </summary>
        public bool IsEnabled => Logger != null;

        /// <summary>
        ///     Check if a given severity level is current enabled for tracing.
        /// </summary>
        public bool IsSeverityEnabled(Severity severity)
        {
            return IsEnabled && severity >= Logger.CurrentSeverity;
        }

        /// <summary>
        ///     Log a message if current severity is set to at least Always.
        /// </summary>
        public void Always(string message)
        {
            TraceMessage(Severity.Always, message);
        }

        /// <summary>
        ///     Log a message if current severity is set to at least Error.
        /// </summary>
        public void Error(string message)
        {
            TraceMessage(Severity.Error, message);
        }

        /// <summary>
        ///     Log a message if current severity is set to at least Warning.
        /// </summary>
        public void Warning(string message)
        {
            TraceMessage(Severity.Warning, message);
        }

        /// <summary>
        ///     Log a message if current severity is set to at least Info.
        /// </summary>
        public void Info(string message)
        {
            TraceMessage(Severity.Info, message);
        }

        /// <summary>
        ///     Log a message if current severity is set to at least Debug.
        /// </summary>
        public void Debug(string message)
        {
            TraceMessage(Severity.Debug, message);
        }

        /// <summary>
        ///     Trace a message if current severity is set to at least the given severity.
        /// </summary>
        public void TraceMessage(Severity severity, string message)
        {
            if (Logger == null)
            {
                return;
            }

            if (Logger is IStructuredLogger structuredLogger)
            {
                structuredLogger.Log(severity, _idAsString, message);
            }
            else
            {
                Logger.Log(severity, $"{_idAsString} {message}");
            }
        }

        /// <summary>
        /// Trace operation completion.
        /// </summary>
        public void OperationFinished(string message, string operationName, string componentName, ResultBase result, TimeSpan duration, Severity successSeverity, OperationKind kind)
        {
            // Severity level for non-successful case is computed in the following way:
            // CriticalFailures -> Error
            // Non-critical errors during initialization -> Warning
            // Non-critical errors in all the other operations -> Info

            var severity = successSeverity;
            if (!result.Succeeded)
            {
                severity = result.IsCriticalFailure ? Severity.Error : (kind == OperationKind.Startup ? Severity.Warning : Severity.Info);
            }

            TraceMessage(severity, message);

            if (Logger is IOperationLogger operationLogger)
            {
                var operationResult = new OperationResult(message, operationName, componentName, statusFromResult(result), duration, kind, result.Exception, Id, severity);
                operationLogger.OperationFinished(operationResult);
            }

            static OperationStatus statusFromResult(ResultBase resultBase)
            {
                if (resultBase.IsCriticalFailure)
                {
                    return OperationStatus.CriticalFailure;
                }
                else if (resultBase.IsCancelled)
                {
                    return OperationStatus.Cancelled;
                }
                else if (!resultBase.Succeeded)
                {
                    return OperationStatus.Failure;
                }
                else
                {
                    return OperationStatus.Success;
                }
            }
        }

        /// <nodoc />
        public void TrackMetric(string name, long value, string tracerName)
        {
            if (Logger is IOperationLogger operationLogger)
            {
                var metric = new Metric(name, value, tracerName);
                operationLogger.TrackMetric(metric);
            }
        }

        /// <nodoc />
        public void TrackTopLevelStatistic(string name, long value)
        {
            if (Logger is IOperationLogger operationLogger)
            {
                var stat = new Statistic(name, value);
                operationLogger.TrackTopLevelStatistic(stat);
            }
        }

        /// <nodoc />
        public void RegisterBuildId(string buildId)
        {
            if (Logger is IOperationLogger operationLogger)
            {
                operationLogger.RegisterBuildId(buildId);
            }
        }

        /// <nodoc />
        public void UnregisterBuildId()
        {
            if (Logger is IOperationLogger operationLogger)
            {
                operationLogger.UnregisterBuildId();
            }
        }
    }
}
