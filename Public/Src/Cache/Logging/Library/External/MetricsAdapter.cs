using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.Logging.External
{
    /// <summary>
    ///     Implements an <see cref="IOperationLogger"/> that dispatches <see cref="ILogger"/> functions to a specific
    ///     instance, and <see cref="IOperationLogger"/>-only operations to a different instance.
    /// </summary>
    /// <remarks>
    ///     The inner loggers are considered to not be owned by the current class. This means we do not dispose them.
    /// </remarks>
    public sealed class MetricsAdapter : IOperationLogger, IStructuredLogger
    {
        private readonly IStructuredLogger _logger;
        private readonly IOperationLogger _operations;

        /// <nodoc />
        public MetricsAdapter(IStructuredLogger logger, IOperationLogger operations)
        {
            Contract.RequiresNotNull(logger);
            Contract.RequiresNotNull(operations);
            Contract.Requires(logger != operations);

            _logger = logger;
            _operations = operations;
        }

        /// <inheritdoc />
        public Severity CurrentSeverity => _logger.CurrentSeverity;

        /// <inheritdoc />
        public int ErrorCount => _logger.ErrorCount;

        /// <inheritdoc />
        public void Diagnostic(string messageFormat, params object[] messageArgs) => _logger.Diagnostic(messageFormat, messageArgs);

        /// <inheritdoc />
        public void Debug(string messageFormat, params object[] messageArgs) => _logger.Debug(messageFormat, messageArgs);

        /// <inheritdoc />
        public void Debug(Exception exception) => _logger.Debug(exception);

        /// <inheritdoc />
        public void Info(string messageFormat, params object[] messageArgs) => _logger.Info(messageFormat, messageArgs);

        /// <inheritdoc />
        public void Warning(string messageFormat, params object[] messageArgs) => _logger.Warning(messageFormat, messageArgs);

        /// <inheritdoc />
        public void Error(string messageFormat, params object[] messageArgs) => _logger.Error(messageFormat, messageArgs);

        /// <inheritdoc />
        public void Error(Exception exception, string messageFormat, params object[] messageArgs) => _logger.Error(exception, messageFormat, messageArgs);

        /// <inheritdoc />
        public void ErrorThrow(Exception exception, string messageFormat, params object[] messageArgs) => _logger.Error(exception, messageFormat, messageArgs);

        /// <inheritdoc />
        public void Fatal(string messageFormat, params object[] messageArgs) => _logger.Fatal(messageFormat, messageArgs);

        /// <inheritdoc />
        public void Always(string messageFormat, params object[] messageArgs) => _logger.Always(messageFormat, messageArgs);

        /// <inheritdoc />
        public void Flush() => _logger.Flush();

        /// <inheritdoc />
        public void Log(Severity severity, string message) => _logger.Log(severity, message);

        /// <inheritdoc />
        public void LogFormat(Severity severity, string messageFormat, params object[] messageArgs) => _logger.LogFormat(severity, messageFormat, messageArgs);

        /// <inheritdoc />
        public void Log(Severity severity, string correlationId, string message) => _logger.Log(severity, correlationId, message);

        /// <inheritdoc />
        public void LogOperationFinished(in OperationResult result)
        {
            // Need to call the both loggers, because the first one will write to the file and
            // will emit telemetry and the operations logger will write to MDM.
            _logger.LogOperationFinished(result);
            _operations.OperationFinished(result);
        }

        /// <inheritdoc />
        public void OperationFinished(in OperationResult result) => _operations.OperationFinished(result);

        /// <inheritdoc />
        public void TrackMetric(in Metric metric) => _operations.TrackMetric(metric);

        /// <inheritdoc />
        public void TrackTopLevelStatistic(in Statistic statistic) => _operations.TrackTopLevelStatistic(statistic);

        /// <inheritdoc />
        public void RegisterBuildId(string buildId) => _operations.RegisterBuildId(buildId);

        /// <inheritdoc />
        public void UnregisterBuildId() => _operations.UnregisterBuildId();

        /// <inheritdoc />
        public void Dispose()
        {
            // NOTE(jubayard): this doesn't dispose anything because it doesn't own the inner loggers.
        }
    }
}
