// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Processes.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Processes
{
    public class EBPFDaemonRetryTests : SandboxedProcessTestBase
    {
        public EBPFDaemonRetryTests(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
        }

        [Fact]
        public async Task SucceedsAfterRetries()
        {
            int operationCallCount = 0;
            int cleanupCallCount = 0;

            var result = await EBPFDaemon.RetryOperationAsync(
                operation: () =>
                {
                    operationCallCount++;
                    if (operationCallCount < EBPFDaemon.MaxAttempts)
                    {
                        return Task.FromResult(new Possible<Unit>(new Failure<string>($"Failure on attempt {operationCallCount}")));
                    }
                    return Task.FromResult(new Possible<Unit>(Unit.Void));
                },
                cleanup: () =>
                {
                    cleanupCallCount++;
                    return Task.CompletedTask;
                },
                maxAttempts: EBPFDaemon.MaxAttempts,
                loggingContext: LoggingContext,
                cancellationToken: CancellationToken.None);

            XAssert.IsTrue(result.Succeeded);
            XAssert.AreEqual(EBPFDaemon.MaxAttempts, operationCallCount);
            // Cleanup is called before each retry (not before the first attempt)
            XAssert.AreEqual(EBPFDaemon.MaxAttempts - 1, cleanupCallCount);

            // Verify retry log events were emitted
            AssertVerboseEventLogged(LogEventId.EBPFDaemonRetrying, EBPFDaemon.MaxAttempts - 1);
        }

        [Fact]
        public async Task FailsAfterAllRetriesExhausted()
        {
            int operationCallCount = 0;
            int cleanupCallCount = 0;

            var result = await EBPFDaemon.RetryOperationAsync(
                operation: () =>
                {
                    operationCallCount++;
                    return Task.FromResult(new Possible<Unit>(new Failure<string>($"Persistent failure {operationCallCount}")));
                },
                cleanup: () =>
                {
                    cleanupCallCount++;
                    return Task.CompletedTask;
                },
                maxAttempts: 3,
                loggingContext: LoggingContext,
                cancellationToken: CancellationToken.None);

            XAssert.IsFalse(result.Succeeded);
            XAssert.AreEqual(EBPFDaemon.MaxAttempts, operationCallCount);
            XAssert.AreEqual(EBPFDaemon.MaxAttempts - 1, cleanupCallCount);

            // Verify retries-exhausted log event was emitted
            AssertWarningEventLogged(LogEventId.EBPFDaemonRetriesExhausted);

            // Verify the last failure message is preserved
            XAssert.Contains(result.Failure!.Describe(), $"Persistent failure {EBPFDaemon.MaxAttempts}");
        }

        [Fact]
        public async Task DoesNotRetryOnCancellation()
        {
            int operationCallCount = 0;
            int cleanupCallCount = 0;
            var cts = new CancellationTokenSource();

            var result = await EBPFDaemon.RetryOperationAsync(
                operation: () =>
                {
                    operationCallCount++;
                    // Cancel after first attempt
#pragma warning disable AsyncFixer02 // Long-running or blocking operations inside an async method

                    cts.Cancel();
#pragma warning restore AsyncFixer02 // Long-running or blocking operations inside an async method

                    return Task.FromResult(new Possible<Unit>(new Failure<string>("Failure before cancellation")));
                },
                cleanup: () =>
                {
                    cleanupCallCount++;
                    return Task.CompletedTask;
                },
                maxAttempts: 3,
                loggingContext: LoggingContext,
                cancellationToken: cts.Token);

            XAssert.IsFalse(result.Succeeded);
            // Should have stopped after one attempt due to cancellation
            XAssert.AreEqual(1, operationCallCount);
            XAssert.AreEqual(0, cleanupCallCount);

            // No retries-exhausted event since we stopped due to cancellation
            AssertWarningEventLogged(LogEventId.EBPFDaemonRetriesExhausted, 0);
        }

        [Fact]
        public async Task CleanupIsCalledBeforeEachRetry()
        {
            var callOrder = new List<string>();

            var result = await EBPFDaemon.RetryOperationAsync(
                operation: () =>
                {
                    callOrder.Add("operation");
                    // Fail all attempts
                    return Task.FromResult(new Possible<Unit>(new Failure<string>("always fail")));
                },
                cleanup: () =>
                {
                    callOrder.Add("cleanup");
                    return Task.CompletedTask;
                },
                maxAttempts: 3,
                loggingContext: LoggingContext,
                cancellationToken: CancellationToken.None);

            XAssert.IsFalse(result.Succeeded);
            // Expected order: operation, cleanup, operation, cleanup, operation
            XAssert.AreEqual(5, callOrder.Count);
            XAssert.AreEqual("operation", callOrder[0]);
            XAssert.AreEqual("cleanup", callOrder[1]);
            XAssert.AreEqual("operation", callOrder[2]);
            XAssert.AreEqual("cleanup", callOrder[3]);
            XAssert.AreEqual("operation", callOrder[4]);

            AssertWarningEventLogged(LogEventId.EBPFDaemonRetriesExhausted, 1);
        }
    }
}