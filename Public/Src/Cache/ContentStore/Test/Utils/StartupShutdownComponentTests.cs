// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Utils
{
    public class StartupShutdownComponentTests
    {
        [Fact]
        public async Task RunShutdownOnlyForStartedComponents()
        {
            var context = new OperationContext(new Context(TestGlobal.Logger));

            var nested1 = new StartupShutdownMock();
            var nested2 = new StartupShutdownMock() { StartupShouldFail = true };
            var nested3 = new StartupShutdownMock();

            var component = new Component(nested1, nested2, nested3);

            await component.StartupAsync(context).ShouldBeError();

            nested1.StartupCompleted.Should().BeTrue();
            nested2.StartupCompleted.Should().BeTrue();
            nested3.StartupStarted.Should().BeFalse("The last component's startup should not be called.");

            await component.ShutdownAsync(context).ShouldBeSuccess();
            nested1.ShutdownCompleted.Should().BeTrue();
            nested2.ShutdownCompleted.Should().BeTrue();

            nested3.ShutdownWasCalled.Should().BeFalse();
            nested3.ShutdownCompleted.Should().BeFalse();
        }

        private class Component : StartupShutdownComponentBase
        {
            /// <inheritdoc />
            protected override Tracer Tracer { get; } = new Tracer("Tracer");

            public Component(params IStartupShutdownSlim[] nestedComponents)
            {
                foreach (var nested in nestedComponents)
                {
                    LinkLifetime(nested);
                }
            }
        }

        private class StartupShutdownMock : StartupShutdownBase
        {
            public bool StartupShouldFail { get; set; }
            public bool ShutdownWasCalled { get; private set; }

            /// <inheritdoc />
            protected override Tracer Tracer { get; } = new Tracer("Tracer");

            /// <inheritdoc />
            protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
            {
                ShutdownWasCalled = true;
                return BoolResult.SuccessTask;
            }

            /// <inheritdoc />
            protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
            {
                if (StartupShouldFail)
                {
                    return Task.FromResult(new BoolResult("Failure"));
                }

                return BoolResult.SuccessTask;
            }
        }
    }
}
