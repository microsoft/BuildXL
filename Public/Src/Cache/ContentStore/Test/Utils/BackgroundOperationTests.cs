// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
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
    public class BackgroundOperationTests : TestBase
    {
        public BackgroundOperationTests(ITestOutputHelper output = null)
            : base(TestGlobal.Logger, output)
        {
        }

        [Fact]
        public async Task Shutdown_Triggers_Cancellation_On_Given_Context()
        {
            var context = new OperationContext(new Context(TestGlobal.Logger));

            bool isCancellationRequested = false;
            BackgroundOperation bo = null;
            bo = new BackgroundOperation(
                "MyOperation",
                async context =>
                {
                    Output.WriteLine("Waiting");

                    // Waiting when for the shutdown to start.
                    await ParallelAlgorithms.WaitUntilOrFailAsync(
                        () => bo.ShutdownStarted,
                        TimeSpan.FromMilliseconds(1),
                        timeout: TimeSpan.FromSeconds(1));

                    Output.WriteLine($"IsCancellationRequested: {context.Token.IsCancellationRequested}");
                    
                    isCancellationRequested = context.Token.IsCancellationRequested;
                    return BoolResult.Success;
                });

            await bo.StartupAsync(context).ShouldBeSuccess();

            await bo.ShutdownAsync(context).ShouldBeSuccess();

            isCancellationRequested.Should().BeTrue();
        }
    }
}
