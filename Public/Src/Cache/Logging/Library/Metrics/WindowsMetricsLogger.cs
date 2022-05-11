// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
#if MICROSOFT_INTERNAL

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using Microsoft.Cloud.InstrumentationFramework;

#nullable enable

namespace BuildXL.Cache.Logging
{
    /// <summary>
    /// Implements metric logger on top of the Ifx MeasureMetric.  Logs to MDM.
    /// </summary>
    internal sealed class WindowsMetricLogger : MetricLogger
    {
        private static readonly Tracer Tracer = new Tracer(nameof(WindowsMetricLogger));

        /// <summary>
        /// A pool for dimension arrays. Using 'Pool' and not 'ArrayPool' because this is lighter weight and we always need to have the exact number of elements.
        /// </summary>
        private static readonly Pool<string[]> DimensionsArrayPool = new Pool<string[]>(factory: () => new string[2]);

        private readonly Context _context;
        private readonly string _logicalNameSpace;
        private readonly string _metricName;

        private readonly MeasureMetric? _measureMetric;
        private readonly MeasureMetric0D? _measureMetric0D;

        /// <nodoc />
        private WindowsMetricLogger(
            Context context,
            string logicalNameSpace,
            string metricName,
            MeasureMetric? measureMetric,
            MeasureMetric0D? measureMetric0D)
        {
            Contract.Requires(measureMetric is not null || measureMetric0D is not null);

            _context = context;
            _logicalNameSpace = logicalNameSpace;
            _metricName = metricName;

            _measureMetric = measureMetric;
            _measureMetric0D = measureMetric0D;
        }

        /// <summary>
        /// Creates an instance of a metric logger.
        /// </summary>
        /// <returns>
        /// May return <see cref="NoOpMetricLogger"/> if <paramref name="monitoringAccount"/> is null or empty or if the initialization fails.
        /// Otherwise returns an instance of an Ifx metric logger for logging to mdm.
        /// </returns>
        public static MetricLogger Create(
            Context context,
            string? monitoringAccount,
            string logicalNameSpace,
            string metricName,
            bool addDefaultDimensions,
            IEnumerable<Dimension> dimensions)
        {
            if (string.IsNullOrEmpty(monitoringAccount))
            {
                return NoOpMetricLogger.Instance;
            }

            // For some reason the generic MeasureMetric class does not work with 0 dimensions.  There is a special
            // class (MeasureMetric0D) that does metrics without any dimensions.
            var dimensionNames = dimensions.Select(d => d.Name).ToArray();
            Tracer.Debug(context, $"Initializing Mdm logger {logicalNameSpace}:{metricName}");
            var error = new ErrorContext();

            MeasureMetric? measureMetric = null;
            MeasureMetric0D? measureMetric0D = null;

            // Very important not to forget to pass true for addDefaultDimension argument since the default is false.
            if (dimensionNames.Length > 0)
            {
                measureMetric = MeasureMetric.Create(monitoringAccount, logicalNameSpace, metricName, ref error, addDefaultDimension: addDefaultDimensions, dimensionNames);
            }
            else
            {
                measureMetric0D = MeasureMetric0D.Create(monitoringAccount, logicalNameSpace, metricName, ref error, addDefaultDimension: addDefaultDimensions);
            }

            if (error.ErrorCode != 0)
            {
                Tracer.Error(context, $"Fail to create MeasureMetric. {logicalNameSpace}:{metricName} ErrorCode: {error.ErrorCode} ErrorMessage: {error.ErrorMessage}. Metrics would be disabled!");
                return NoOpMetricLogger.Instance;
            }

            return new WindowsMetricLogger(context, logicalNameSpace, metricName, measureMetric, measureMetric0D);
        }

        /// <summary>
        /// Initialize the default metric dimensions.
        /// </summary>
        public static void InitializeDefaultDimensions(Context context, List<DefaultDimension> defaultDimensions)
        {
            Tracer.Debug(context, $"Setting default dimensions: {string.Join(", ", defaultDimensions.Select(d => d.ToString()))}");

            try
            {
                var defaultDimensionNames = defaultDimensions.Select(d => d.Name).ToArray();
                var defaultDimensionValues = defaultDimensions.Select(d => d.Value).ToArray();

                if (defaultDimensionNames.Length > 0)
                {
                    var error = new ErrorContext();
                    if (
                        !DefaultConfiguration.SetDefaultDimensionNamesValues(
                            ref error,
                            (uint)defaultDimensionNames.Length,
                            defaultDimensionNames,
                            defaultDimensionValues))
                    {
                        Tracer.Warning(
                            context,
                            $"Unable to set MDM default dimensions.  ErrorCode: {error.ErrorCode} ErrorMessage: {error.ErrorMessage}");
                    }
                }
            }
            catch (Exception exception)
            {
                // Not the end of the world if we can't initialize these.  Only aggregate totals will be 
                // available if this fails (e.g. no aggregation by environment available).
                Tracer.Warning(context, $"Unable to initialize default MDM dimensions: {exception}.");
            }
        }

        /// <inheritdoc />
        public override void Log(long metricValue, params string?[] dimensionValues)
        {
            ReplaceNullAndEmpty(dimensionValues);

            LogCore(metricValue, dimensionValues!);
        }

        /// <inheritdoc />
        public override void Log(long metricValue, string dimension1, string dimension2)
        {
            using var handle = DimensionsArrayPool.Get();

            var dimensions = handle.Value;
            dimensions[0] = dimension1;
            dimensions[1] = dimension2;
            LogCore(metricValue, dimensions);
        }

        private void LogCore(long metricValue, string[] dimensionValues)
        {
            var error = new ErrorContext();

            // One of the two fields is not null.
            bool success = _measureMetric?.LogValue(metricValue, ref error, dimensionValues) ??
                           _measureMetric0D!.LogValue(metricValue, ref error);

            if (!success)
            {
                Tracer.Error(_context,
                    $"Fail to log metric value. MetricValue='{metricValue}', DimensionValues='{string.Join(", ", dimensionValues)}', {_logicalNameSpace}:{_metricName} ErrorCode: {error.ErrorCode} ErrorMessage: {error.ErrorMessage}");
            }
        }
    }
}

#endif
