// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using BuildXL.Utilities.Core;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public sealed class MemoryConservationTests
    {
        [Fact]
        public void TransitionsOnEntryAndAfterReentry()
        {
            var memoryConservation = new MemoryConservation();
            var target = new TestMemoryConservationTarget();
            memoryConservation.Register(target);

            Assert.True(memoryConservation.Enter());
            Assert.False(memoryConservation.Enter());

            Assert.True(memoryConservation.IsActive);
            Assert.True(target.IsActive);
            Assert.Equal(1, target.ActivationCount);

            Thread.Sleep(10);
            memoryConservation.Exit(force: true);
            Assert.False(target.IsActive);
            Assert.True(memoryConservation.Enter());

            Assert.True(target.IsActive);
            Assert.Equal(2, target.ActivationCount);
            Thread.Sleep(10);
            memoryConservation.Exit(force: true);

            Assert.Equal(2, memoryConservation.Counters.GetCounterValue(MemoryConservationCounter.ActivationCount));
            Assert.Equal(2, memoryConservation.Counters.GetCounterValue(MemoryConservationCounter.GarbageCollectionCount));
            Assert.True(memoryConservation.Counters.GetElapsedTime(MemoryConservationCounter.ActiveDuration) > TimeSpan.Zero);
        }

        [Fact]
        public void RepeatedEntryDoesNotNotifyTargets()
        {
            int collectionCount = 0;
            var memoryConservation = new MemoryConservation(TimeSpan.Zero, () => collectionCount++);
            var target = new TestMemoryConservationTarget();
            memoryConservation.Register(target);

            memoryConservation.Enter();
            memoryConservation.Enter();

            Assert.Equal(1, target.ActivationCount);
            Assert.Equal(1, collectionCount);
            Assert.Equal(1, memoryConservation.Counters.GetCounterValue(MemoryConservationCounter.GarbageCollectionCount));

            memoryConservation.Exit();
            memoryConservation.Enter();

            Assert.Equal(2, collectionCount);
            Assert.Equal(2, memoryConservation.Counters.GetCounterValue(MemoryConservationCounter.GarbageCollectionCount));
        }

        [Fact]
        public void CollectionRunsAfterTargetsReleaseMemory()
        {
            var target = new TestMemoryConservationTarget();
            var memoryConservation = new MemoryConservation(
                TimeSpan.Zero,
                () => Assert.True(target.IsActive));
            memoryConservation.Register(target);

            memoryConservation.Enter();
        }

        [Fact]
        public void MinimumActiveDurationPreventsFlapping()
        {
            var memoryConservation = new MemoryConservation(TimeSpan.FromHours(1));
            var target = new TestMemoryConservationTarget();
            memoryConservation.Register(target);

            memoryConservation.Enter();

            Assert.False(memoryConservation.Exit());
            Assert.True(memoryConservation.IsActive);
            Assert.True(target.IsActive);

            Assert.True(memoryConservation.Exit(force: true));
            Assert.False(memoryConservation.IsActive);

            var noDelayMemoryConservation = new MemoryConservation(TimeSpan.Zero);
            noDelayMemoryConservation.Enter();

            Assert.True(noDelayMemoryConservation.Exit());
            Assert.False(noDelayMemoryConservation.IsActive);
        }

        [Fact]
        public void DisabledContextCopiesWithoutMemoryConservation()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            using var cancellationTokenSource = new CancellationTokenSource();
            var copiedContext = BuildXLContext.CreateInstanceForTestingWithCancellationToken(context, cancellationTokenSource.Token);

            Assert.Null(context.MemoryConservation);
            Assert.Null(copiedContext.MemoryConservation);
            Assert.Equal(cancellationTokenSource.Token, copiedContext.CancellationToken);
        }

        [Fact]
        public void EnabledContextCopiesMemoryConservation()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var memoryConservation = context.EnableMemoryConservation();
            using var cancellationTokenSource = new CancellationTokenSource();
            var copiedContext = BuildXLContext.CreateInstanceForTestingWithCancellationToken(context, cancellationTokenSource.Token);

            Assert.Same(memoryConservation, context.MemoryConservation);
            Assert.Same(context.MemoryConservation, copiedContext.MemoryConservation);
            Assert.Equal(cancellationTokenSource.Token, copiedContext.CancellationToken);
        }

        private sealed class TestMemoryConservationTarget : IMemoryConservationTarget
        {
            public bool IsActive { get; private set; }

            public int ActivationCount { get; private set; }

            public void OnMemoryConservationStateChanged(bool isActive)
            {
                IsActive = isActive;
                if (isActive)
                {
                    ActivationCount++;
                }
            }
        }
    }
}
