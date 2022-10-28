// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

namespace BuildXL.Cache.Logging
{
    /// <summary>
    /// A no-op metric logger that is used when the metrics are disabled.
    /// </summary>
    internal sealed class NoOpMetricLogger : MetricLogger
    {
        /// <summary>
        /// A global instance of a no-op metrics logger.
        /// </summary>
        public static NoOpMetricLogger Instance { get; } = new NoOpMetricLogger();

        /// <inheritdoc />
        public override void Log(long metricValue, params string?[] dimensionValues) { }

        /// <inheritdoc />
        public override void Log(long metricValue, string dimension1, string dimension2) { }

        /// <inheritdoc />
        public override void Log(in OperationFinishedMetric metric) { }
    }
}
