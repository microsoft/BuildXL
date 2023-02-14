// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;

// ReSharper disable UnusedMemberInSuper.Global

// disable 'Missing XML comment for publicly visible type' warnings.
#pragma warning disable 1591
#pragma warning disable SA1600 // Elements must be documented

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    /// Global configuration that controls some aspects of tracing, like whether to trace statistics.
    /// </summary>
    public static class GlobalTracerConfiguration
    {
        /// <summary>
        /// If true the statistics is traced at components shutdown.
        /// </summary>
        public static bool EnableTraceStatisticsAtShutdown { get; set; } = true;
    }

    public class Tracer
    {
        // If this flag is set, then the trace name will be used in all the tracing operations.
        private const int DefaultArgsPerLog = 500;

        private int _numberOfRecoverableErrors;
        private int _numberOfCriticalErrors;
        private readonly LogManager _logManager;

        public bool LogOperationStarted { get; set; } = true;

        /// <summary>
        /// A name of a tracer.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Number of recoverable errors (i.e. exceptions) occurred during the lifetime of a tracer.
        /// </summary>
        public int NumberOfRecoverableErrors => _numberOfRecoverableErrors;

        /// <summary>
        /// Number of non-recoverable (i.e. critical exceptions) occurred during the lifetime of a tracer.
        /// </summary>
        public int NumberOfCriticalErrors => _numberOfCriticalErrors;

        public Tracer(string name, LogManager? logManager = null)
        {
            Contract.Requires(name != null);
            Name = name;
            _logManager = logManager ?? LogManager.Instance;
        }

        /// <nodoc />
        internal void RaiseCriticalError(ResultBase result)
        {
            Interlocked.Increment(ref _numberOfCriticalErrors);
            CriticalErrorsObserver.RaiseCriticalError(result);
        }

        /// <nodoc />
        internal void RaiseRecoverableError(ResultBase result)
        {
            Interlocked.Increment(ref _numberOfRecoverableErrors);
            CriticalErrorsObserver.RaiseRecoverableError(result);
        }

        public virtual void Always(Context context, string message, [CallerMemberName] string? operation = null)
        {
            Trace(Severity.Always, context, message, operation);
        }

        public void Error(Context context, string message, [CallerMemberName] string? operation = null)
        {
            Trace(Severity.Error, context, message, operation);
        }

        public void Error(Context context, Exception? exception, string message, [CallerMemberName] string? operation = null)
        {
            Trace(Severity.Error, context, message, operation, exception);
        }

        public void Warning(Context context, Exception? exception, string message, [CallerMemberName] string? operation = null)
        {
            Trace(Severity.Warning, context, message, operation, exception: exception);
        }

        public void Warning(Context context, string message, [CallerMemberName] string? operation = null)
        {
            Trace(Severity.Warning, context, message, operation);
        }

        public void Info(Context context, string message, [CallerMemberName] string? operation = null)
        {
            Trace(Severity.Info, context, message, operation);
        }

        public void Info(Context context, Exception? exception, string message, [CallerMemberName] string? operation = null)
        {
            Trace(Severity.Info, context, message, operation, exception);
        }

        public void Debug(Context context, string message, [CallerMemberName] string? operation = null)
        {
            Trace(Severity.Debug, context, message, operation);
        }

        public void Debug(Context context, Exception? exception, string message, [CallerMemberName] string? operation = null)
        {
            Trace(Severity.Debug, context, message, operation, exception);
        }

        public void Diagnostic(Context context, string message, [CallerMemberName] string? operation = null)
        {
            Trace(Severity.Diagnostic, context, message, operation);
        }

        public void Diagnostic(Context context, Exception? exception, string message, [CallerMemberName] string? operation = null)
        {
            Trace(Severity.Diagnostic, context, message, operation, exception);
        }

        public void Trace(Severity severity, Context context, string message, string? operation = null, Exception? exception = null)
        {
            var configuration = _logManager.GetOperationConfiguration(Name, operation);

            if (configuration is null)
            {
                // No configuration is available.
                context.TraceMessage(severity, message, exception, component: Name, operation: operation);
            }
            else
            {
                // Tracing if not disabled
                if (configuration.Disabled != true &&
                    (configuration.ErrorsOnly != true || severity >= Severity.Error))
                {
                    context.TraceMessage(severity, message, exception, component: Name, operation: operation);
                }
            }
        }

        public virtual void StartupStart(Context context)
        {
            Debug(context, $"{Name}.Startup start");
        }

        public virtual void StartupStop(Context context, BoolResult result)
        {
            InitializationFinished(context, result, result.Duration, $"{Name}.Startup stop {result.DurationMs}ms result=[{result}]", "StartupStop");
        }

        public virtual void ShutdownStart(Context context)
        {
            Debug(context, $"{Name}.Shutdown start");
        }

        public virtual void ShutdownStop(Context context, BoolResult result)
        {
            TracerOperationFinished(context, result, $"{Name}.Shutdown stop {result.DurationMs}ms result=[{result}]");
        }

        /// <summary>
        /// Helper method for tracing message with large amount of instances.
        /// </summary>
        protected void TraceBulk<T>(string baseMessage, IReadOnlyList<T> items, Func<T, string> itemPrinter, Action<string> printAction)
        {
            TraceBulk(baseMessage, items, items.Count, itemPrinter, printAction);
        }

        /// <summary>
        /// Helper method for tracing message with large amount of instances.
        /// </summary>
        protected void TraceBulk<T>(string baseMessage, IEnumerable<T> items, int itemsCount, Func<T, string> itemPrinter, Action<string> printAction)
        {
            if (itemsCount <= DefaultArgsPerLog)
            {
                // Trace all the information at once if we can.
                string message = $"{baseMessage}[{string.Join(",", items.Select(item => itemPrinter(item)))}]";
                printAction(message);
            }
            else
            {
                // the input is too big to print in one shot.
                int pageNumber = 0;
                foreach (var page in items.GetPages(DefaultArgsPerLog))
                {
                    // Printing in the following form:
                    // Base message: (100/725) [item1, item2, item3 ...]
                    string message = $"{baseMessage} ({(pageNumber + 1) * DefaultArgsPerLog}/{itemsCount}) [{string.Join(",", page.Select(item => itemPrinter(item)))}]";
                    printAction(message);

                    pageNumber++;
                }
            }
        }

        public virtual void GetStatsStart(Context context)
        {
            Debug(context, $"{Name}.GetStats start", operation: "GetStats");
        }

        public virtual void GetStatsStop(Context context, GetStatsResult result)
        {
            TracerOperationFinished(context, result, $"{Name}.GetStats stop {result.DurationMs}ms result=[{result}]", callerName: "GetStats");

            if (result.Succeeded)
            {
                foreach (var counter in result.CounterSet.Counters)
                {
                    if (!string.IsNullOrWhiteSpace(counter.MetricName))
                    {
                        context.TrackTopLevelStatistic(counter.MetricName, counter.ValueAsLong);
                    }
                }
            }
        }

        public void OperationStarted(Context context, string operationName, bool enabled = true, string? additionalInfo = null)
        {
            if (LogOperationStarted && enabled)
            {
                var message = $"{Name}.{operationName} start.";
                if (!string.IsNullOrWhiteSpace(additionalInfo))
                {
                    message = $"{message} {additionalInfo}";
                }

                Severity severity = Severity.Debug;
                context.OperationStarted(message, operationName, Name, severity, OperationKind.None);
            }
        }

        /// <summary>
        /// Trace a start of an operation.
        /// </summary>
        /// <remarks>
        /// Used only from legacy tracers.
        /// </remarks>
        public void TraceOperationStarted(Context context, string additionalInfo, [CallerMemberName]string? callerName = null)
        {
            OperationStarted(context, operationName: callerName!, enabled: true, additionalInfo);
        }

        /// <summary>
        /// Trace results of an operation.
        /// </summary>
        /// <remarks>
        /// Used only from legacy tracers.
        /// </remarks>
        public void TracerOperationFinished(Context context, ResultBase result, string message, Severity successSeverity = Severity.Debug, [CallerMemberName]string? callerName = null)
        {
            OperationFinishedCore(context, result, result.Duration, message, OperationKind.None, successSeverity, callerName!);
        }

        /// <summary>
        /// Creates a message text based on a given result, duration, caller name and an optional message.
        /// </summary>
        public string CreateMessageText(ResultBase result, TimeSpan duration, string message, string callerName)
        {
            if (!string.IsNullOrEmpty(message))
            {
                message = " " + message;
            }

            string resultToString = result.IsCancelled ? "OperationCancelled" : result.ToString();
            return $"{Name}.{callerName} stop {duration.TotalMilliseconds}ms. result=[{resultToString}].{message}";
        }

        /// <summary>
        /// Trace startup/initialization operation.
        /// </summary>
        public void InitializationFinished(Context context, ResultBase result, TimeSpan duration, string message, string operationName)
        {
            OperationFinishedCore(context, result, duration, message, OperationKind.Startup, Severity.Debug, operationName);
        }

        private void OperationFinishedCore(Context context, ResultBase result, TimeSpan duration, string message, OperationKind operationKind, Severity successSeverity, string operationName)
        {
            if (result.IsCriticalFailure)
            {
                message = $"Critical error occurred: {message}.";
                RaiseCriticalError(result);
            }
            else if (result.Exception != null)
            {
                RaiseRecoverableError(result);
            }

            context.OperationFinished(message, operationName, Name, result, duration, successSeverity, operationKind);
        }

        /// <summary>
        /// Track metric with a given name and a value in MDM.
        /// </summary>
        public void TrackMetric(Context context, string name, long value, bool warnOnNegativeValue = true)
        {
            if (value < 0)
            {
                if (warnOnNegativeValue)
                {
                    // We can't trace negative metrics.
                    Warning(context, $"Failed logging metric '{name}' with a negative value '{value}'");
                }
            }
            else
            {
                context.TrackMetric(name, value, Name);
            }
        }

        /// <nodoc />
        public void OperationFinished(OperationContext context, ResultBase result, TimeSpan duration, [CallerMemberName]string? operationName = null, bool traceErrorsOnly = false)
        {
            OperationFinished(context.TracingContext, result, duration, message: string.Empty, operationName, traceErrorsOnly: traceErrorsOnly);
        }

        /// <nodoc />
        public void OperationFinished(Context context, ResultBase result, TimeSpan duration, string message, [CallerMemberName]string? operationName = null, bool traceErrorsOnly = false)
        {
            // Intentionally using a separate argument but not result.DurationMs, because result.DurationMs is mutable and not necessarily set.
            if (context.IsEnabled)
            {
                var messageText = context.RequiresMessage(result, traceErrorsOnly)
                    ? CreateMessageText(result, duration, message, operationName!)
                    : "none";

                OperationFinishedCore(context, result, duration, messageText, OperationKind.None, traceErrorsOnly ? Severity.Diagnostic : Severity.Debug, operationName!);
            }
        }

        /// <inheritdoc cref="GlobalTracerConfiguration.EnableTraceStatisticsAtShutdown"/>
        public bool EnableTraceStatisticsAtShutdown => GlobalTracerConfiguration.EnableTraceStatisticsAtShutdown;

        /// <summary>
        /// Trace stats during component's shutdown.
        /// </summary>
        public void TraceStatisticsAtShutdown(Context context, CounterSet counterSet, string? prefix = null)
        {
            if (EnableTraceStatisticsAtShutdown)
            {
                if (string.IsNullOrEmpty(prefix))
                {
                    // Always prepend the messages with 'Shutdown stats' to simplify filtering.
                    // It's possible to filter based on the operation name, but seeing a prefix like this one in every message
                    // makes the filtering and analysis easier.
                    counterSet.LogOrderedNameValuePairs(s => Trace(Severity.Debug, context, $"Shutdown stats: {s}", operation: nameof(TraceStatisticsAtShutdown)));
                }
                else
                {
                    counterSet.LogOrderedNameValuePairs(s => Trace(Severity.Debug, context, $"Shutdown stats: {prefix}.{s}", operation: nameof(TraceStatisticsAtShutdown)));
                }
            }
        }
    }
}
