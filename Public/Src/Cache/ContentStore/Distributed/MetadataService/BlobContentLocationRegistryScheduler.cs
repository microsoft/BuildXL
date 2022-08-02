// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Serialization;
using RocksDbSharp;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public interface IBlobContentLocationRegistryScheduler
    {
        Task WaitForStartStageAsync(OperationContext context, int stage, DateTime start);
    }

    /// <summary>
    /// Synchronizes stages among multiple machines by aligning time boundaries (i.e. if update interval is 15 min and stage interval is 1 min)
    /// stages are aligned to offsets of 1 min from 15 min intervals in time
    /// i.e.
    /// stage 0 starts at 0, 15, 30, 45 min past the hour
    /// stage 1 starts at 1, 16, 31, 46 min past the hour
    /// stage 2 starts at 2, 17, 32, 47 min past the hour
    /// </summary>
    public record BlobContentLocationRegistryScheduler(BlobContentLocationRegistryConfiguration Configuration, IClock Clock) : IBlobContentLocationRegistryScheduler
    {
        public Task WaitForStartStageAsync(OperationContext context, int stage, DateTime start)
        {
            return WaitForStartStageAsync(context, stage, start, targetTime: new AsyncOut<DateTime>());
        }

        public virtual Task WaitForStartStageAsync(OperationContext context, int stage, DateTime start, AsyncOut<DateTime> targetTime)
        {
            if (Configuration.StageInterval == TimeSpan.Zero)
            {
                return Task.CompletedTask;
            }

            var now = Clock.UtcNow;
            var alignedStart = AlignFloor(start, Configuration.PartitionsUpdateInterval);
            if (alignedStart < start)
            {
                alignedStart += Configuration.PartitionsUpdateInterval;
            }

            targetTime.Value = alignedStart + Configuration.StageInterval.Value.Multiply(stage);

            var delay = targetTime.Value - Clock.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                return Clock.Delay(delay, context.Token);
            }

            return Task.CompletedTask;
        }

        public static DateTime AlignFloor(DateTime value, TimeSpan interval)
        {
            return new DateTime((value.Ticks / interval.Ticks) * interval.Ticks);
        }
    }
}
