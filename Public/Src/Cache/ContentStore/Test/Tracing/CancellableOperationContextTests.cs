// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.Test.Tracing
{
    public class CancellableOperationContextTests : TestBase
    {
        public CancellableOperationContextTests(ITestOutputHelper output = null)
            : base(TestGlobal.Logger, output)
        {
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DisposeShouldNotTriggerCancellationForCancellableToken(bool cancellableToken)
        {
            var context = new Context(TestGlobal.Logger);
            // There is very important distinction between "cancellable" and "non-cancellable" tokens.
            // The old implementation was making the distinction when cts.Token.CanBeCanceled was true or not.
            // So we want to cover both cases here.

            // CancellationToken.None is not cancellable.
            var contextToken = cancellableToken ? new CancellationTokenSource().Token : CancellationToken.None;
            var operationContext = new OperationContext(context, contextToken);
            var secondaryTokenSource = new CancellationTokenSource();

            bool scopeCancellationWasCalled = false;
            using (var scope = new CancellableOperationContext(operationContext, secondaryTokenSource.Token))
            {
                scope.Context.Token.Register(
                    () =>
                    {
                        scopeCancellationWasCalled = true;
                    });
            }

            scopeCancellationWasCalled.Should().BeFalse("The cancellation should not be triggered by CancellableOperationContext.Dispose");
        }

        [Fact]
        public void ScopeCancellationTriggersCancellationWhenContextCancellationIsSet()
        {
            var context = new Context(TestGlobal.Logger);
            var contextTokenSource = new CancellationTokenSource();
            var operationContext = new OperationContext(context, contextTokenSource.Token);
            var secondaryTokenSource = new CancellationTokenSource();

            bool scopeCancellationWasCalled = false;
            using (var scope = new CancellableOperationContext(operationContext, secondaryTokenSource.Token))
            {
                scope.Context.Token.Register(
                    () =>
                    {
                        scopeCancellationWasCalled = true;
                    });

                contextTokenSource.Cancel();
            }

            scopeCancellationWasCalled.Should().BeTrue();
        }

        [Fact]
        public void ScopeCancellationTriggersCancellationWhenSecondaryCancellationIsSet()
        {
            var context = new Context(TestGlobal.Logger);
            var contextTokenSource = new CancellationTokenSource();
            var operationContext = new OperationContext(context, contextTokenSource.Token);
            var secondaryTokenSource = new CancellationTokenSource();

            bool scopeCancellationWasCalled = false;
            using (var scope = new CancellableOperationContext(operationContext, secondaryTokenSource.Token))
            {
                scope.Context.Token.Register(
                    () =>
                    {
                        scopeCancellationWasCalled = true;
                    });

                secondaryTokenSource.Cancel();
            }

            scopeCancellationWasCalled.Should().BeTrue();
        }
    }
}
