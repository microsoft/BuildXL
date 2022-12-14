// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using System.Diagnostics.ContractsLight;
using System.Threading;
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
        /// If true, then the nested contexts will use tree-like hierarchical ids, like parent_Id.1 parent_id.2 etc.
        /// </summary>
        public static bool UseHierarchicalIds = false;

        private int _currentChildId;
        
        private readonly string _idAsString;
        private const int MaxIdLength = 100;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Context"/> class.
        /// </summary>
        /// <param name="logger">
        ///     Caller's logger to be invoked as processing occurs.
        /// </param>
        public Context(ILogger logger)
            : this(Guid.NewGuid().ToString(), logger)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Context"/> class.
        /// </summary>
        public Context(Guid id, ILogger logger)
        {
            Logger = logger;
            _idAsString = id.ToString();
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Context"/> class.
        /// </summary>
        public Context(string id, ILogger logger)
        {
            Logger = logger;
            _idAsString = id;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Context"/> class.
        /// </summary>
        public Context(Context other, string? componentName = null, [CallerMemberName]string? caller = null)
            : this(other, CreateNestedId(other), componentName, caller)
        {
        }
        
        /// <summary>
        ///     Initializes a new instance of the <see cref="Context"/> class.
        /// </summary>
        public Context(Context other, string id, string? componentName, [CallerMemberName]string? caller = null)
            : this(id, other.Logger)
        {
            string prefix = caller!;
            if (!string.IsNullOrEmpty(componentName))
            {
                prefix = string.Concat(componentName, ".", prefix);
            }

            Debug($"{prefix}: {other._idAsString} parent to {_idAsString}", component: componentName ?? string.Empty, operation: caller);
        }

        /// <nodoc />
        public Context CreateNested(string componentName, [CallerMemberName]string? caller = null)
        {
            return new Context(this, componentName, caller);
        }

        /// <nodoc />
        public Context CreateNested(string id, string componentName, [CallerMemberName]string? caller = null)
        {
            return new Context(this, id, componentName, caller);
        }

        private static string CreateNestedId(Context other)
        {
            var traceId = other.TraceId;
            if (traceId.Length >= MaxIdLength || !UseHierarchicalIds)
            {
                // A safeguard to avoid indefinite growth of the id.
                // Or hierarchical ids are disabled.
                return Guid.NewGuid().ToString();
            }
            
            return string.Concat(traceId, ".", Interlocked.Increment(ref other._currentChildId).ToString());
        }

        /// <summary>
        ///     Gets the unique Id.
        /// </summary>
        public string TraceId => _idAsString;

        /// <summary>
        ///     Gets the associated tracing logger.
        /// </summary>
        public ILogger Logger { get; private set; }

        /// <summary>
        ///     Replaces an existing logger with a given one.
        /// </summary>
        public void ReplaceLogger(ILogger logger)
        {
            // The logger can be null, but the new logger must not be null, otherwise
            // we could have a race condition and fail with NRE, when the old logger was not null and 'IsEnabled'
            // property is true.
            Contract.Requires(logger != null);
            Logger = logger;
        }

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
        public void Always(string message, string component, [CallerMemberName] string? operation = null)
        {
            TraceMessage(Severity.Always, message, component, operation);
        }

        /// <summary>
        ///     Log a message if current severity is set to at least Error.
        /// </summary>
        public void Error(string message, string component, [CallerMemberName] string? operation = null)
        {
            TraceMessage(Severity.Error, message, component, operation);
        }

        /// <summary>
        ///     Log a message if current severity is set to at least Error.
        /// </summary>
        public void Error(Exception exception, string message, string component, [CallerMemberName] string? operation = null)
        {
            TraceMessage(Severity.Error, message, exception, component, operation);
        }

        /// <summary>
        ///     Log a message if current severity is set to at least Warning.
        /// </summary>
        public void Warning(string message, string component, [CallerMemberName] string? operation = null)
        {
            TraceMessage(Severity.Warning, message, component, operation);
        }

        /// <summary>
        ///     Log a message if current severity is set to at least Warning.
        /// </summary>
        public void Warning(Exception exception, string message, string component, [CallerMemberName] string? operation = null)
        {
            TraceMessage(Severity.Warning, message, exception, component, operation);
        }

        /// <summary>
        ///     Log a message if current severity is set to at least Info.
        /// </summary>
        public void Info(string message, string component, [CallerMemberName] string? operation = null)
        {
            TraceMessage(Severity.Info, message, component, operation);
        }

        /// <summary>
        ///     Log a message if current severity is set to at least Debug.
        /// </summary>
        public void Debug(string message, string component, [CallerMemberName] string? operation = null)
        {
            TraceMessage(Severity.Debug, message, component, operation);
        }

        /// <summary>
        ///     Trace a message if current severity is set to at least the given severity.
        /// </summary>
        public void TraceMessage(Severity severity, string message, string component, [CallerMemberName] string? operation = null)
        {
            TraceMessage(severity, message, exception: null, component: component, operation: operation);
        }

        /// <summary>
        ///     Trace a message if current severity is set to at least the given severity.
        /// </summary>
        public void TraceMessage(Severity severity, string message, Exception? exception, string component, [CallerMemberName] string? operation = null)
        {
            if (Logger == null)
            {
                return;
            }

            if (!IsSeverityEnabled(severity))
            {
                return;
            }

            operation ??= string.Empty;

            if (Logger is IStructuredLogger structuredLogger)
            {
                structuredLogger.Log(new LogMessage(message, operation, component, OperationKind.None, _idAsString, severity, exception));
            }
            else
            {
                string? provenance;
                if (string.IsNullOrEmpty(component) || string.IsNullOrEmpty(operation))
                {
                    provenance = $"{component}{operation}: ";

                    if (provenance.Equals(": "))
                    {
                        provenance = string.Empty;
                    }
                }
                else
                {
                    provenance = $"{component}.{operation}: ";
                }

                if (exception == null)
                {
                    Logger.Log(severity, $"{_idAsString} {provenance}{message}");
                }
                else
                {
                    if (severity == Severity.Error)
                    {
                        Logger.Error(exception, $"{_idAsString} {provenance}{message}");
                    }
                    else
                    {
                        Logger.Log(severity, $"{_idAsString} {provenance}{message} {exception}");
                    }
                }
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
                TraceMessage(severity, message, componentName, operationName);
            }
        }

        /// <summary>
        /// Gets whether the message is required
        /// </summary>
        public bool RequiresMessage(ResultBase result, bool traceErrorsOnly)
        {
            // Diagnostic level message is not on in prod, so in practice the end message factory is not
            // called when 'traceErrorsOnly' flag is true.
            return !traceErrorsOnly || !result.Succeeded || IsSeverityEnabled(Severity.Diagnostic);
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
            if (Logger is IStructuredLogger structuredLogger && IsSeverityEnabled(severity))
            {
                // Note, that 'message' here is a plain message from the client
                // without correlation id.
                var operationResult = new OperationResult(message, operationName, componentName, result.GetStatus(), duration, kind, result.Exception, _idAsString, severity);
                structuredLogger.LogOperationFinished(operationResult);
                messageWasTraced = true;
            }
            else if (Logger is IOperationLogger operationLogger)
            {
                var operationResult = new OperationResult(message, operationName, componentName, result.GetStatus(), duration, kind, result.Exception, _idAsString, severity);
                operationLogger.OperationFinished(operationResult);
            }

            if (!messageWasTraced)
            {
                TraceMessage(severity, message, componentName, operationName);
            }
        }

        /// <summary>
        /// Emits mdm metrics for a finished operation.
        /// </summary>
        /// <remarks>
        /// This is useful when a component is on a critical path and we can't afford using the full logging infrastructure but still want to trace the duration and the count.
        /// </remarks>
        public void TrackOperationFinishedMetrics(string operationName, string componentName, TimeSpan duration)
        {
            if (Logger is IOperationLogger operationLogger)
            {
                var operationResult = new OperationResult(
                    "Empty message",
                    operationName,
                    componentName,
                    OperationStatus.Success,
                    duration,
                    OperationKind.None,
                    exception: null,
                    _idAsString,
                    Severity.Info);
                operationLogger.OperationFinished(operationResult);
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

        /// <nodoc />
        public static void ChangeRole(string role)
        {
            GlobalInfoStorage.SetGlobalInfo(GlobalInfoKey.LocalLocationStoreRole, role);
        }
    }
}
