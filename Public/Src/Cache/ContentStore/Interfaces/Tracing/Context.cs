// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;

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
        public Context(Context other, string? componentName = null, [CallerMemberName]string? caller = null)
            : this(other, Guid.NewGuid(), componentName, caller)
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

        /// <summary>
        ///     Initializes a new instance of the <see cref="Context"/> class.
        /// </summary>
        public Context(Context other, Guid id, string? componentName, [CallerMemberName]string? caller = null)
            : this(id, other.Logger)
        {
            string prefix = caller!;
            if (!string.IsNullOrEmpty(componentName))
            {
                prefix = string.Concat(componentName, ".", prefix);
            }

            Debug($"{prefix}: {other._idAsString} parent to {_idAsString}");
        }

        /// <nodoc />
        public Context CreateNested(string? componentName = null, [CallerMemberName]string? caller = null)
        {
            return new Context(this, componentName, caller);
        }

        /// <nodoc />
        public Context CreateNested(Guid id, string? componentName = null, [CallerMemberName]string? caller = null)
        {
            return new Context(this, id, componentName, caller);
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
        /// Trace operation start.
        /// </summary>
        public void OperationStarted(
            string message,
            string operationName,
            string componentName,
            Severity severity,
            OperationKind kind)
        {
            if (Logger is IStructuredLogger structuredLogger)
            {
                // Note, that 'message' here is a plain message from the client
                // without correlation id.
                var operation = new OperationStarted(message, operationName, componentName, kind, _idAsString, severity);
                structuredLogger.LogOperationStarted(operation);
            }
            else
            {
                TraceMessage(severity, message);
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

            bool messageWasTraced = false;

            // The Logger instance may implement both IOperationLogger and IStructuredLogger
            // (this is actually the case right now for the new logging infrastructure).
            // And to get correct behavior in this case we need to check IStructuredLogger
            // first because that kind of logger does two things:
            // 1. Traces the operation and
            // 2. Emit metrics.
            // But other IOperationLogger implementation just trace the metrics and a text racing should be done separately.
            if (Logger is IStructuredLogger structuredLogger)
            {
                // Note, that 'message' here is a plain message from the client
                // without correlation id.
                var operationResult = new OperationResult(message, operationName, componentName, statusFromResult(result), duration, kind, result.Exception, _idAsString, severity);
                structuredLogger.LogOperationFinished(operationResult);
                messageWasTraced = true;
            }
            else if (Logger is IOperationLogger operationLogger)
            {
                var operationResult = new OperationResult(message, operationName, componentName, statusFromResult(result), duration, kind, result.Exception, _idAsString, severity);
                operationLogger.OperationFinished(operationResult);
            }

            if (!messageWasTraced)
            {
                TraceMessage(severity, message);
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
        public void ChangeRole(string role)
        {
            GlobalInfoStorage.SetGlobalInfo(GlobalInfoKey.LocalLocationStoreRole, role);
        }

        /// <nodoc />
        public void RegisterBuildId(string buildId)
        {
            Logger.RegisterBuildId(buildId);
        }

        /// <nodoc />
        public void UnregisterBuildId()
        {
            Logger.UnregisterBuildId();
        }
    }

    /// <nodoc />
    public static class LoggerExtensions
    {
        /// <summary>
        /// Sets build id as an ambient information used by tracing infrastructure.
        /// </summary>
        public static void RegisterBuildId(this ILogger logger, string buildId)
        {
            GlobalInfoStorage.SetGlobalInfo(GlobalInfoKey.BuildId, buildId);

            if (logger is IOperationLogger operationLogger)
            {
                operationLogger.RegisterBuildId(buildId);
            }
        }

        /// <summary>
        /// Clears an existing build id set by <see cref="RegisterBuildId"/>.
        /// </summary>
        public static void UnregisterBuildId(this ILogger logger)
        {
            GlobalInfoStorage.SetGlobalInfo(GlobalInfoKey.BuildId, value: null);

            if (logger is IOperationLogger operationLogger)
            {
                operationLogger.UnregisterBuildId();
            }
        }
    }
}
