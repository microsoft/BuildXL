// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

#nullable enable

namespace BuildXL.Cache.Logging
{
    /// <summary>
    /// Geneva-based metrics logger.
    /// </summary>
    public sealed class MdmOperationLogger : FailingLogger, IOperationLogger
    {
        private const string ServiceName = "ContentAddressableStoreService";
        private readonly ConcurrentDictionary<string, MetricLogger> _statisticLoggers = new ();

        private readonly Context _context;
        private readonly string? _monitoringAccount;
        private readonly MetricLogger _operationFinishedMetricLogger;
        private static readonly Dimension[] OperationDurationDimensions =
        {
            Dimensions.Operation,
            Dimensions.OperationKind,
            Dimensions.OperationSuccess,
            Dimensions.FailureKind,
            Dimensions.Component,
            Dimensions.ExceptionType
        };

        private readonly MetricLogger _metricLogger;
        private static readonly Dimension[] MetricDimensions = { Dimensions.Metric, Dimensions.Component };

        private MdmOperationLogger(Context context, string? monitoringAccount, MetricLogger operationFinishedMetricLogger, MetricLogger metricLogger)
        {
            _context = context;
            _monitoringAccount = monitoringAccount;
            _operationFinishedMetricLogger = operationFinishedMetricLogger;
            _metricLogger = metricLogger;
        }

        /// <summary>
        /// Creates an instance of <see cref="MdmOperationLogger"/> for a given <paramref name="monitoringAccount"/>.
        /// </summary>
        /// <remarks>
        /// The mdm metrics are off if <paramref name="monitoringAccount"/> is null or empty.
        /// </remarks>
        public static MdmOperationLogger Create(
            Context context,
            string? monitoringAccount,
            List<DefaultDimension> defaultDimensions,
            bool saveMetricsAsynchronously)
        {
            // Setting the default dimensions once instead of passing them all the time explicitly.
            MetricLogger.InitializeMdmDefaultDimensions(context, defaultDimensions);

            // Add prefix for 'logicalNameSpaces' if this feature will be used in CI.
            var operationFinishedMetricLogger = MetricLogger.CreateLogger(
                context,
                monitoringAccount,
                logicalNameSpace: ServiceName,
                metricName: "OperationDurationMs",
                addDefaultDimensions: true,
                dimensions: OperationDurationDimensions,
                saveMetricsAsynchronously);

            var metricLogger = MetricLogger.CreateLogger(
                context,
                monitoringAccount,
                logicalNameSpace: ServiceName,
                metricName: "Metric",
                addDefaultDimensions: true,
                dimensions: MetricDimensions,
                saveMetricsAsynchronously);

            return new MdmOperationLogger(context, monitoringAccount, operationFinishedMetricLogger, metricLogger);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            _metricLogger.Dispose();
        }

        /// <inheritdoc />
        public void OperationFinished(in OperationResult result)
        {
            _operationFinishedMetricLogger.Log(
                new OperationFinishedMetric(
                (long)result.Duration.TotalMilliseconds,
                result.OperationName,
                result.OperationKind.ToStringNoAlloc(),
                result.Status == OperationStatus.Success ? "Succeeded" : "Failed",
                result.Status.ToStringNoAlloc(),
                result.TracerName,
                    result.Exception?.GetType().ToString() ?? "NA"));
        }

        /// <inheritdoc />
        public void TrackMetric(in Metric metric)
        {
            _metricLogger.Log(metric.Value, metric.Name, metric.TracerName);
        }

        /// <inheritdoc />
        public void TrackTopLevelStatistic(in Statistic statistic)
        {
            Contract.Requires(!string.IsNullOrEmpty(statistic.Name), "Cannot log a metric with null or empty name to MDM");

            var logger = getOrCreateMetricLogger(statistic.Name);
            logger.Log(statistic.Value);

            MetricLogger getOrCreateMetricLogger(string statisticName)
            {
                if (!_statisticLoggers.TryGetValue(statisticName, out var metricLogger))
                {
                    metricLogger = MetricLogger.CreateLogger(
                        _context,
                        _monitoringAccount,
                        logicalNameSpace: ServiceName,
                        metricName: statisticName,
                        addDefaultDimensions: true,
                        dimensions: Array.Empty<Dimension>(),
                        saveMetricsAsynchronously: false); // we should not have too many stats metrics, so its fine to save them synchronously all the time.

                    _statisticLoggers[statisticName] = metricLogger;
                }

                return metricLogger;
            }
        }

        /// <inheritdoc />
        public void RegisterBuildId(string buildId)
        {
            // no op.
            // GlobalInfoStorage should be used directly instead.
        }

        /// <inheritdoc />
        public void UnregisterBuildId()
        {
            // no op.
            // GlobalInfoStorage should be used directly instead.
        }
    }
}
