// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

#nullable enable

namespace BuildXL.Cache.Logging
{
    /// <summary>
    /// Represents a metric emitted when an operation is finished.
    /// </summary>
    public record struct OperationFinishedMetric(
        long DurationMs,
        string OperationName,
        string OperationKind,
        string SuccessOrFailure,
        string Status,
        string Component,
        string ExceptionType);

    /// <summary>
    /// A base type for logging metrics.
    /// </summary>
    public abstract class MetricLogger : IDisposable
    {
        /// <summary>
        /// Logs metric value with the given dimensions.
        /// </summary>
        /// <param name="metricValue">Metric value.</param>
        /// <param name="dimensionValues">Dimension values.</param>
        public abstract void Log(long metricValue, params string?[] dimensionValues);

        /// <summary>
        /// Logs a metric of a finished operation.
        /// </summary>
        public abstract void Log(in OperationFinishedMetric metric);

        /// <summary>
        /// Logs metric value with the given dimensions.
        /// </summary>
        public abstract void Log(long metricValue, string dimension1, string dimension2);

        /// <summary>
        /// Dispose remaining logging resources or flush the logs.
        /// </summary>
        public virtual void Dispose() { }

        /// <summary>
        /// Geneva / MDM is not tolerant of null or empty dimension values. Replace them with actual strings to prevent errors.
        /// </summary>
        protected static void ReplaceNullAndEmpty(string?[] dimensionValues)
        {
            for (int i = 0; i < dimensionValues.Length; i++)
            {
                if (dimensionValues[i] is null)
                {
                    dimensionValues[i] = "NULL";
                }
                // The compiler can't figure out that the previous 'if' block
                // makes the 'i-th' element not-nullable.
                else if (dimensionValues[i]!.Length == 0)
                {
                    dimensionValues[i] = "EMPTY";
                }
            }
        }

        /// <summary>
        /// Initialize the default metric dimensions
        /// </summary>
        public static void InitializeMdmDefaultDimensions(Context context, List<DefaultDimension> defaultDimensions)
        {
#if MICROSOFT_INTERNAL
            WindowsMetricLogger.InitializeDefaultDimensions(context, defaultDimensions);
#endif
        }

        /// <summary>
        /// Creates an instance of a metric logger.
        /// </summary>
        /// <returns>
        /// May return <see cref="NoOpMetricLogger"/> if <paramref name="monitoringAccount"/> is null or empty or if the initialization fails.
        /// Otherwise returns an instance of an Ifx metric logger for logging to mdm.
        /// </returns>
        public static MetricLogger CreateLogger(
            Context context,
            string? monitoringAccount,
            string logicalNameSpace,
            string metricName,
            bool addDefaultDimensions,
            IEnumerable<Dimension> dimensions,
            bool saveMetricsAsynchronously)
        {
#if MICROSOFT_INTERNAL
            return WindowsMetricLogger.Create(context, monitoringAccount, logicalNameSpace, metricName, addDefaultDimensions, dimensions, saveMetricsAsynchronously);
#else
            return NoOpMetricLogger.Instance;
#endif
        }
    }
}
