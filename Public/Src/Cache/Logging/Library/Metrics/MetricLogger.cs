// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

#nullable enable

namespace BuildXL.Cache.Logging
{
    /// <summary>
    /// A base type for logging metrics.
    /// </summary>
    public abstract class MetricLogger
    {
        /// <summary>
        /// Logs metric value with the given dimensions.
        /// </summary>
        /// <param name="metricValue">Metric value.</param>
        /// <param name="dimensionValues">Dimension values.</param>
        public abstract void Log(long metricValue, params string?[] dimensionValues);

        /// <summary>
        /// Logs metric value with the given dimensions.
        /// </summary>
        public abstract void Log(long metricValue, string dimension1, string dimension2);

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
            IEnumerable<Dimension> dimensions)
        {
#if MICROSOFT_INTERNAL
            return WindowsMetricLogger.Create(context, monitoringAccount, logicalNameSpace, metricName, addDefaultDimensions, dimensions);
#else
            return NoOpMetricLogger.Instance;
#endif
        }
    }
}
