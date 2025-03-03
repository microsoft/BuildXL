// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.BlobLifetimeManager.Library
{
    public class BlobLifetimeManagerStatisticsCollector : StartupShutdownComponentBase
    {
        protected override Tracer Tracer { get; } = new(nameof(BlobLifetimeManagerStatisticsCollector));

        private readonly MachinePerformanceCollector _machinePerformanceCollector;

        private readonly TimeSpan _reportFrequency;

        public BlobLifetimeManagerStatisticsCollector(TimeSpan? reportFrequency = null)
        {
            _reportFrequency = reportFrequency ?? TimeSpan.FromMinutes(5);

            _machinePerformanceCollector = new MachinePerformanceCollector(
                // The collection frequency is set to half of the report frequency to ensure that we have enough data
                // points.
                collectionFrequency: _reportFrequency.Multiply(0.5),
                // The system doesn't typically run under Windows, so WMI is most often unavailable. 
                logWmiCounters: false);
            RunInBackground(nameof(LogPerformanceStatistics), LogPerformanceStatistics, fireAndForget: true);
        }

        protected override Task<BoolResult> ShutdownComponentAsync(OperationContext context)
        {
            _machinePerformanceCollector.Dispose();
            return base.ShutdownComponentAsync(context);
        }

        private async Task<BoolResult> LogPerformanceStatistics(OperationContext context)
        {
            while (!context.Token.IsCancellationRequested)
            {
                var performanceStatistics = _machinePerformanceCollector.GetMachinePerformanceStatistics();
                Tracer.Info(context, $"MachinePerformanceStatistics: {performanceStatistics.ToTracingString()}");

                try
                {
                    await Task.Delay(_reportFrequency, context.Token);
                }
                catch (TaskCanceledException)
                {
                    // Ignore cancellation
                }
            }

            return BoolResult.Success;
        }
    }
}
