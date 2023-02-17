// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using Microsoft.Cloud.InstrumentationFramework;
using Xunit;
using OperationResult = BuildXL.Cache.ContentStore.Interfaces.Logging.OperationResult;

#nullable enable

namespace BuildXL.Cache.Logging.Test
{
    public class MdmOperationLoggerTests
    {
        [Fact]
        public void MetricOperationsAreNotFailing()
        {
            // We can't check if the metric is uploaded successfully, because a special agent should be running on the machine,
            // but we can check that all the required dependencies are available.
            // And this test just checks that all the managed and native dlls are presented and all the types can be loaded successfully.
            var context = new Context(new Logger());
            using var logger = MdmOperationLogger.Create(context, "CloudBuildCBTest", new List<DefaultDimension>(), saveMetricsAsynchronously: false);
            logger.OperationFinished(new OperationResult("CustomMessage", "Test OperationName", "TestTracer", OperationStatus.Success, TimeSpan.FromSeconds(42), OperationKind.None, exception: null, operationId: "42", severity: Severity.Debug));
        }

        [Fact]
        public void MetricOperationsAreNotFailingWhenSavingAsynchronously()
        {
            var context = new Context(new Logger());
            using var logger = MdmOperationLogger.Create(context, "CloudBuildCBTest", new List<DefaultDimension>(), saveMetricsAsynchronously: true);
            logger.OperationFinished(new OperationResult("CustomMessage", "Test OperationName", "TestTracer", OperationStatus.Success, TimeSpan.FromSeconds(42), OperationKind.None, exception: null, operationId: "42", severity: Severity.Debug));
        }

        [Fact]
        public void TestAsyncOperationFinished()
        {
            var logger = CreateLogger(new Context(new Logger()));

            var inputMetrics = Enumerable.Range(1, 5_005).Select(
                    v => new OperationFinishedMetric(
                        v,
                        $"OperationName{v}",
                        $"OperationKind{v}",
                        $"SuccessOrFailure{v}",
                        $"Status{v}",
                        $"Component{v}",
                        $"ExceptionType{v}"))
                .ToArray();
            int currentThreadId = Environment.CurrentManagedThreadId;
            int onLogCoreThreadId = -1;
            var savedMetrics = new List<OperationFinishedMetric>();
            logger.OnLogCore = tuple =>
                               {
                                   onLogCoreThreadId = Environment.CurrentManagedThreadId;
                                   var dimensions = tuple.dimensions;
                                   savedMetrics.Add(new OperationFinishedMetric(tuple.metricValue,
                                       dimensions[0],
                                       dimensions[1],
                                       dimensions[2],
                                       dimensions[3],
                                       dimensions[4],
                                       dimensions[5]));
                               };

            foreach (var input in inputMetrics)
            {
                logger.Log(input);
            }

            logger.Dispose();

            Assert.Equal(inputMetrics, savedMetrics);
            Assert.NotEqual(currentThreadId, onLogCoreThreadId);
        }

        [Fact]
        public void TestAsyncLog2DimensionsStress()
        {
            int i = 0;
            try
            {
                for (i = 0; i < 1000; i++)
                {
                    TestAsyncLog2Dimensions();
                }
            }
            catch (Exception e)
            {
                throw new Exception($"I: {i}", e);
            }
        }

        [Fact]
        public void TestAsyncLog2Dimensions()
        {
            var logger = CreateLogger(new Context(new Logger()));

            var inputMetrics = Enumerable.Range(1, 5_005).Select(v => new TestMetric(v, Dimensions.Generate(dimensionLength: 2, v))).ToArray();

            int currentThreadId = Environment.CurrentManagedThreadId;
            int onLogCoreThreadId = -1;

            var savedMetrics = new List<TestMetric>();
            logger.OnLogCore = tuple =>
                               {
                                   onLogCoreThreadId = Environment.CurrentManagedThreadId;
                                   // Need to make a copy of 'dimensions' array because the array is pooled.
                                   savedMetrics.Add(new TestMetric(tuple.metricValue, new Dimensions(tuple.dimensions.ToArray())));
                               };

            foreach (var input in inputMetrics)
            {
                logger.Log(input.Value, input.Dimensions.DimensionValues[0], input.Dimensions.DimensionValues[1]);
            }

            logger.Dispose();

            Assert.Equal(savedMetrics.ToArray(), inputMetrics);
            Assert.NotEqual(currentThreadId, onLogCoreThreadId);
        }

        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        [InlineData(15)]
        [Theory]
        public void TestAsyncLogDimensions(int dimensionLength)
        {
            var logger = CreateLogger(new Context(new Logger()));

            var inputMetrics = Enumerable.Range(1, 5_005).Select(v => new TestMetric(v, Dimensions.Generate(dimensionLength, v))).ToArray();

            int currentThreadId = Environment.CurrentManagedThreadId;
            int onLogCoreThreadId = -1;

            var savedMetrics = new List<TestMetric>();
            logger.OnLogCore = tuple =>
                               {
                                   onLogCoreThreadId = Environment.CurrentManagedThreadId;
                                   savedMetrics.Add(new TestMetric(tuple.metricValue, new Dimensions(tuple.dimensions)));
                               };

            foreach (var input in inputMetrics)
            {
                logger.Log(input.Value, input.Dimensions.DimensionValues);
            }

            logger.Dispose();

            Assert.Equal(inputMetrics, savedMetrics);
            Assert.NotEqual(currentThreadId, onLogCoreThreadId);
        }

        private static WindowsMetricLoggerTester CreateLogger(Context context)
        {
            var error = new ErrorContext();
            string logicalNameSpace = "logicalNameSpace";
            string metricName = "metricName";
            var measureMetric = MeasureMetric.Create("monitoringAccount", logicalNameSpace, metricName, ref error, addDefaultDimension: true, Array.Empty<string>());

            return new WindowsMetricLoggerTester(
                context,
                logicalNameSpace,
                metricName,
                measureMetric: measureMetric,
                measureMetric0D: null,
                saveMetricsAsynchronously: true);
        }

        private class WindowsMetricLoggerTester : WindowsMetricLogger
        {
            public Action<(long metricValue, string[] dimensions)>? OnLogCore;

            public WindowsMetricLoggerTester(Context context, string logicalNameSpace, string metricName, MeasureMetric? measureMetric, MeasureMetric0D? measureMetric0D, bool saveMetricsAsynchronously)
                : base(context, logicalNameSpace, metricName, measureMetric, measureMetric0D, saveMetricsAsynchronously)
            {
            }

            /// <inheritdoc />
            protected override void LogCore(long metricValue, string[] dimensionValues, bool returnToPool)
            {
                OnLogCore?.Invoke((metricValue, dimensionValues));

                if (returnToPool)
                {
                    ReturnToPool(dimensionValues);
                }
            }
        }

        private readonly record struct Dimensions(string[] DimensionValues)
        {
            /// <inheritdoc />
            public bool Equals(Dimensions other)
            {
                return DimensionValues.SequenceEqual(other.DimensionValues);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                int hashCode = 42;
                foreach (var d in DimensionValues)
                {
                    hashCode = HashCodeHelper.Combine(hashCode, d.GetHashCode());
                }

                return hashCode;
            }

            public static Dimensions Generate(int dimensionLength, int seed)
            {
                return new Dimensions(
                    Enumerable.Range(1, dimensionLength)
                        .Select(d => $"D{d}_{seed}")
                        .ToArray());
            }

            private bool PrintMembers(StringBuilder builder)
            {
                builder.Append(string.Join(", ", DimensionValues));
                return true;
            }
        }

        private record struct TestMetric(long Value, Dimensions Dimensions);
    }
}
