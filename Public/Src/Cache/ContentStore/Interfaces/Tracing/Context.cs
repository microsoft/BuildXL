// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
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
        ///     Initializes a new instance of the <see cref="Context"/> class.
        /// </summary>
        /// <param name="logger">
        ///     Caller's logger to be invoked as processing occurs.
        /// </param>
        public Context(ILogger logger)
        {
            Id = Guid.NewGuid();
            Logger = logger;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Context"/> class.
        /// </summary>
        public Context(Guid id, ILogger logger)
        {
            Id = id;
            Logger = logger;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Context"/> class.
        /// </summary>
        public Context(Context other)
            : this(other, Guid.NewGuid())
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Context"/> class.
        /// </summary>
        public Context(Context other, Guid id)
        {
            Id = id;
            Logger = other.Logger;
            Debug($"{other.Id} parent to {Id}");
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
            Logger?.Log(Severity.Always, message);
        }

        /// <summary>
        ///     Log a message if current severity is set to at least Error.
        /// </summary>
        public void Error(string message)
        {
            Logger?.Log(Severity.Error, message);
        }

        /// <summary>
        ///     Log a message if current severity is set to at least Warning.
        /// </summary>
        public void Warning(string message)
        {
            Logger?.Log(Severity.Warning, message);
        }

        /// <summary>
        ///     Log a message if current severity is set to at least Info.
        /// </summary>
        public void Info(string message)
        {
            Logger?.Log(Severity.Info, message);
        }

        /// <summary>
        ///     Log a message if current severity is set to at least Debug.
        /// </summary>
        public void Debug(string message)
        {
            Logger?.Log(Severity.Debug, message);
        }

        /// <summary>
        ///     Trace a message if current severity is set to at least the given severity.
        /// </summary>
        public void TraceMessage(Severity severity, string message)
        {
            Logger?.Log(severity, $"{Id} {message}");
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

            OperationStatus statusFromResult(ResultBase resultBase)
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
