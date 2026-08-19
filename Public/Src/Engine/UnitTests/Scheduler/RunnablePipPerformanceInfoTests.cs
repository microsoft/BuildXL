// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Scheduler;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Utilities.Core;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    public class RunnablePipPerformanceInfoTests
    {
        [Fact]
        public void PackedEnumIndexesAreZeroBasedAndContiguous()
        {
            Assert.Equal(0UL, EnumTraits<PipExecutionStep>.MinValue);
            Assert.Equal(EnumTraits<PipExecutionStep>.MaxValue + 1, (ulong)EnumTraits<PipExecutionStep>.ValueCount);
            Assert.True(EnumTraits<PipExecutionStep>.MaxValue <= byte.MaxValue);
            Assert.Equal((int)EnumTraits<PipExecutionStep>.MaxValue + 1, RunnablePipPerformanceInfo.PipExecutionStepCount);

            Assert.Equal(0UL, EnumTraits<DispatcherKind>.MinValue);
            Assert.Equal(EnumTraits<DispatcherKind>.MaxValue + 1, (ulong)EnumTraits<DispatcherKind>.ValueCount);
            Assert.Equal((int)EnumTraits<DispatcherKind>.MaxValue + 1, RunnablePipPerformanceInfo.DispatcherKindCount);
        }

        [Fact]
        public void AccumulatesStepAndRemoteDurations()
        {
            var performance = new RunnablePipPerformanceInfo(DateTime.UtcNow);

            performance.Executed(PipExecutionStep.CacheLookup, TimeSpan.FromMilliseconds(11));
            performance.Executed(PipExecutionStep.CacheLookup, TimeSpan.FromMilliseconds(13));
            performance.RemoteExecuted(
                workerId: 7,
                step: PipExecutionStep.CacheLookup,
                remoteStepDuration: TimeSpan.FromMilliseconds(17),
                remoteQueueDuration: TimeSpan.FromMilliseconds(19),
                queueRequestDuration: TimeSpan.FromMilliseconds(23),
                grpcDuration: TimeSpan.FromMilliseconds(29));
            performance.RemoteExecuted(
                workerId: 8,
                step: PipExecutionStep.CacheLookup,
                remoteStepDuration: TimeSpan.FromMilliseconds(31),
                remoteQueueDuration: TimeSpan.FromMilliseconds(37),
                queueRequestDuration: TimeSpan.FromMilliseconds(41),
                grpcDuration: TimeSpan.FromMilliseconds(43));

            Assert.Equal(24, performance.GetStepDurationMs(PipExecutionStep.CacheLookup));
            Assert.Equal((uint)8, performance.GetWorkerId(PipExecutionStep.CacheLookup));
            Assert.Equal(48, performance.GetRemoteStepDurationMs(PipExecutionStep.CacheLookup));
            Assert.Equal(56, performance.GetRemoteQueueDurationMs(PipExecutionStep.CacheLookup));
            Assert.Equal(64, performance.GetPipBuildRequestQueueDurationMs(PipExecutionStep.CacheLookup));
            Assert.Equal(72, performance.GetPipBuildRequestGrpcDurationMs(PipExecutionStep.CacheLookup));
            Assert.True(performance.WasExecutedRemotely(PipExecutionStep.CacheLookup));
            Assert.False(performance.WasExecutedRemotely(PipExecutionStep.ExecuteProcess));
        }

        [Fact]
        public void StoresEachDurationInWholeMilliseconds()
        {
            var performance = new RunnablePipPerformanceInfo(DateTime.UtcNow);

            performance.Executed(PipExecutionStep.CacheLookup, TimeSpan.FromTicks(19_000));
            performance.Executed(PipExecutionStep.CacheLookup, TimeSpan.FromTicks(21_000));

            Assert.Equal(3, performance.GetStepDurationMs(PipExecutionStep.CacheLookup));
        }

        [Fact]
        public void PreservesWorkerIdsAndSaturatesDurations()
        {
            var performance = new RunnablePipPerformanceInfo(DateTime.UtcNow);

            performance.RemoteExecuted(
                workerId: uint.MaxValue,
                step: PipExecutionStep.ExecuteProcess,
                remoteStepDuration: TimeSpan.FromMilliseconds(uint.MaxValue),
                remoteQueueDuration: TimeSpan.Zero,
                queueRequestDuration: TimeSpan.Zero,
                grpcDuration: TimeSpan.Zero);
            performance.RemoteExecuted(
                workerId: uint.MaxValue,
                step: PipExecutionStep.ExecuteProcess,
                remoteStepDuration: TimeSpan.FromMilliseconds(1),
                remoteQueueDuration: TimeSpan.Zero,
                queueRequestDuration: TimeSpan.Zero,
                grpcDuration: TimeSpan.Zero);

            Assert.Equal(uint.MaxValue, performance.GetWorkerId(PipExecutionStep.ExecuteProcess));
            Assert.Equal((long)uint.MaxValue, performance.GetRemoteStepDurationMs(PipExecutionStep.ExecuteProcess));
        }
    }
}
