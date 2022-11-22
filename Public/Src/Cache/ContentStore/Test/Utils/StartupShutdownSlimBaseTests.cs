// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.ParallelAlgorithms;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Utils
{
    public class StartupShutdownSlimBaseTests : TestBase
    {
        public StartupShutdownSlimBaseTests(ITestOutputHelper output = null)
            : base(TestGlobal.Logger, output)
        {
        }

        [Fact]
        public async Task TrackShutdown_IsCanceled_OnShutdown()
        {
            var context = new OperationContext(new Context(TestGlobal.Logger));
            var component = new Component();
            using var cancellableContext = component.ShutdownTracker(context);

            bool canceled = false;
            using var _ = cancellableContext.Context.Token.Register(() => { canceled = true; });

            await component.ShutdownAsync(context).ThrowIfFailure();
            canceled.Should().BeTrue();
        }


        [Fact]
        public void TrackShutdown_IsCanceled_OnContextCancellation()
        {
            var cts = new CancellationTokenSource();
            var context = new OperationContext(new Context(TestGlobal.Logger), cts.Token);
            var component = new Component();
            using var cancellableContext = component.ShutdownTracker(context);

            bool canceled = false;
            using var _ = cancellableContext.Context.Token.Register(() => { canceled = true; });

            cts.Cancel();
            canceled.Should().BeTrue();
        }

        [Fact]
        public async Task TrackShutdownWithDelay_IsDelayed_OnShutdown_WhenShutdown_IsAlready_Called()
        {
            var context = new OperationContext(new Context(TestGlobal.Logger));
            var component = new Component();
            using var cancellableContext = component.DelayedShutdownTracker(context, GetDelay());

            await component.ShutdownAsync(context).ThrowIfFailure();
            await AssertCanceledAfterDelay(context, component, cancellableContext.Context.Token, shutdown: false);
        }

        [Fact]
        public async Task TrackShutdownWithDelay_IsDelayed_OnShutdown()
        {
            var context = new OperationContext(new Context(TestGlobal.Logger));
            var component = new Component();
            using var cancellableContext = component.DelayedShutdownTracker(context, GetDelay());

            await AssertCanceledAfterDelay(context, component, cancellableContext.Context.Token);
        }

        [Fact]
        public async Task TrackShutdownWithDelay_IsNotDelayed_OnShutdown_With_Zero_Delay()
        {
            var context = new OperationContext(new Context(TestGlobal.Logger));
            var component = new Component();
            using var cancellableContext = component.DelayedShutdownTracker(context, TimeSpan.Zero);

            bool canceled = false;
            using var _ = cancellableContext.Context.Token.Register(() => { canceled = true; });

            await component.ShutdownAsync(context).ThrowIfFailure();

            canceled.Should().BeTrue();
        }

        private static async Task AssertCanceledAfterDelay(OperationContext context, Component component, CancellationToken token, bool shutdown = true)
        {
            bool canceled = false;
            using var _ = token.Register(() => { canceled = true; });

            if (shutdown)
            {
                await component.ShutdownAsync(context).ThrowIfFailure();
            }

            canceled.Should().BeFalse();

            // Making sure it is called eventually.
            await WaitUntilAsync(() => canceled);
        }

        [Fact]
        public async Task TrackShutdownWithDelay_IsDelayed_When_Cancellation_IsAlreadyTriggered()
        {
            var cts = new CancellationTokenSource();
            var context = new OperationContext(new Context(TestGlobal.Logger), cts.Token);
            var component = new Component();
            cts.Cancel();

            using var cancellableContext = component.DelayedShutdownTracker(context, GetDelay());

            // The cancellation should not happen instantly, instead it should happen after a timeout.
            cancellableContext.Context.Token.IsCancellationRequested.Should().BeFalse();

            await AssertCanceledAfterDelay(context, component, cancellableContext.Context.Token);
        }
        
        private static TimeSpan GetDelay() => TimeSpan.FromMilliseconds(100);

        private static Task WaitUntilAsync(Func<bool> predicate)
        {
            return ParallelAlgorithms.WaitUntilAsync(predicate, pollInterval: TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(10));
        }

        private class Component : StartupShutdownSlimBase
        {
            /// <inheritdoc />
            protected override Tracer Tracer { get; } = new Tracer("Tracer");

            /// <nodoc />
            public CancellableOperationContext ShutdownTracker(OperationContext context)
            {
                return TrackShutdown(context);
            }

            /// <nodoc />
            public CancellableOperationContext DelayedShutdownTracker(OperationContext context, TimeSpan delay)
            {
                return TrackShutdownWithDelayedCancellation(context, delay);
            }
        }
    }
}
