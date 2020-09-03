// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Tasks;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.Test.Tracing
{
    public class PerformOperationTests : TestWithOutput
    {
        public PerformOperationTests(ITestOutputHelper output)
        : base(output)
        {
        }

        [Fact]
        public async Task TraceLongRunningOperationPeriodically()
        {
            var tracer = new Tracer("MyTracer");
            var context = new OperationContext(new Context(TestGlobal.Logger));

            var r = await context.PerformOperationAsync(
                tracer,
                async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    return BoolResult.Success;
                },
                pendingOperationTracingInterval: TimeSpan.FromMilliseconds(100),
                extraStartMessage: "Start message");
            r.ShouldBeSuccess();

            var fullOutput = GetFullOutput();
            fullOutput.Should().Contain("The operation 'TraceLongRunningOperationPeriodically' is not finished yet. Start message: Start message");
        }

        [Fact]
        public async Task TraceLongRunningOperationPeriodicallyUsingDefaultSettings()
        {
            var oldInterval = DefaultTracingConfiguration.DefaultPendingOperationTracingInterval;
            DefaultTracingConfiguration.DefaultPendingOperationTracingInterval = TimeSpan.FromMilliseconds(100);
            try
            {
                var tracer = new Tracer("MyTracer");
                var context = new OperationContext(new Context(TestGlobal.Logger));

                var r = await context.PerformOperationAsync(
                    tracer,
                    async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1));
                        return BoolResult.Success;
                    });
                r.ShouldBeSuccess();

                var fullOutput = GetFullOutput();
                fullOutput.Should().Contain("The operation 'TraceLongRunningOperationPeriodicallyUsingDefaultSettings' is not finished yet");
            }
            finally
            {
                DefaultTracingConfiguration.DefaultPendingOperationTracingInterval = oldInterval;
            }
        }

        [Fact]
        public async Task TestPerformOperationAsyncTimeout()
        {
            var tracer = new Tracer("MyTracer");
            var context = new OperationContext(new Context(TestGlobal.Logger));

            var r = await context.PerformOperationWithTimeoutAsync(
                tracer,
                async nestedContext =>
                {
                    await Task.Delay(TimeSpan.FromMinutes(1));
                    return BoolResult.Success;
                },
                timeout: TimeSpan.FromMilliseconds(100));

            r.ShouldBeError();
            r.IsCancelled.Should().BeFalse("The operation fails with TimeoutException and is not cancelled");
        }

        [Fact]
        public async Task OperationSucceedsWithASmallTimeout()
        {
            var tracer = new Tracer("MyTracer");
            var context = new OperationContext(new Context(TestGlobal.Logger));

            var r = await context.PerformOperationWithTimeoutAsync(
                tracer,
                async nestedContext =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1));
                    return BoolResult.Success;
                },
                timeout: TimeSpan.FromMinutes(10));

            r.ShouldBeSuccess();
        }

        [Fact]
        public async Task TraceWhenWithTimeoutIsCalled()
        {
            var tracer = new Tracer("MyTracer");
            var context = new OperationContext(new Context(TestGlobal.Logger));

            int shortOperationDurationMs = 10;
            TimeSpan timeout = TimeSpan.FromMilliseconds(shortOperationDurationMs * 100);
            var result1 = await context.PerformOperationAsync(
                    tracer,
                    () => operation(shortOperationDurationMs).WithTimeoutAsync(timeout));
            result1.ShouldBeSuccess();

            int longOperationDurationMs = 10_000;
            timeout = TimeSpan.FromMilliseconds(longOperationDurationMs / 100);
            var result2 = await context.PerformOperationAsync(
                tracer,
                () => operation(longOperationDurationMs).WithTimeoutAsync(timeout));
            result2.ShouldBeError();

            var fullOutput = GetFullOutput();
            fullOutput.Should().Contain("TimeoutException");
            async Task<BoolResult> operation(int duration)
            {
                await Task.Delay(duration);
                return BoolResult.Success;
            }
        }

        [Fact]
        public void EndMessageFactoryIsCalledForFailedCase()
        {
            var tracer = new Tracer("MyTracer");
            var context = new OperationContext(new Context(TestGlobal.Logger));

            var result = context.CreateOperation(
                    tracer,
                    () =>
                    {
                        return new Result<int>(new Exception("Error42"));
                    })
                .WithOptions(endMessageFactory: r => r.Succeeded ? "ExtraSuccess" : "ExtraFailure")
                .Run();

            // Check that the exception's stack trace appears in the final output only ones.
            var fullOutput = GetFullOutput();
            fullOutput.Should().Contain("ExtraFailure");
            fullOutput.Should().Contain("Error42");
        }

        [Fact]
        public void TestCriticalErrorsDiagnosticTracedOnlyOnce()
        {
            var tracer = new Tracer("MyTracer");
            var context = new OperationContext(new Context(TestGlobal.Logger));

            Exception exception = null;
            var result = context.CreateOperation(
                tracer,
                () =>
                {
                    exception = GetException();
                    if (exception != null)
                    {
                        throw GetException();
                    }

                    return BoolResult.Success;
                })
                .WithOptions(traceOperationFinished: true)
                .Run();

            // Check that the exception's stack trace appears in the final output only ones.
            var fullOutput = GetFullOutput();
            var firstIndex = fullOutput.IndexOf(result.Diagnostics);
            var lastIndex = fullOutput.LastIndexOf(result.Diagnostics);

            Assert.NotEqual(firstIndex, -1);
            // The first and the last indices should be equal if the output contains a diagnostic message only once.
            firstIndex.Should().Be(lastIndex, "Diagnostic message should appear in the output message only once.");
        }

        [Fact]
        public void TraceSlowSuccessfulOperationsEvenWhenErrorsOnlyFlagIsProvided()
        {
            var tracer = new Tracer("MyTracer");
            var context = new OperationContext(new Context(TestGlobal.Logger));

            // Running a fast operation first
            var result = context.CreateOperation(
                    tracer,
                    () =>
                    {
                        return new CustomResult();

                    })
                .WithOptions(traceErrorsOnly: true)
                .Run(caller: "FastOperation");

            // Check that the exception's stack trace appears in the final output only ones.
            var fullOutput = GetFullOutput();
            fullOutput.Should().NotContain("FastOperation");

            // Running a slow operation now
            result = context.CreateOperation(
                   tracer,
                   () =>
                   {
                        // Making the operation intentionally slow.
                        Thread.Sleep(10);
                       return new CustomResult();

                   })
               .WithOptions(traceErrorsOnly: true, silentOperationDurationThreshold: TimeSpan.FromMilliseconds(0))
               .Run(caller: "SlowOperation");

            // Check that the exception's stack trace appears in the final output only ones.
            fullOutput = GetFullOutput();
            fullOutput.Should().Contain("SlowOperation");
        }

        [Fact]
        public void TraceErrorsOnly()
        {
            var tracer = new Tracer("MyTracer");
            var context = new OperationContext(new Context(TestGlobal.Logger));

            // Running a successful operation first
            var result = context.CreateOperation(
                    tracer,
                    () => new CustomResult())
                .TraceErrorsOnlyIfEnabled(enableTracing: true)
                .Run(caller: "success");

            // The operation should not be traced, because it was successful.
            var fullOutput = GetFullOutput();
            fullOutput.Should().NotContain("success");

            // Running an operation that fails.
            string error = "My Error";

            result = context.CreateOperation(
                    tracer,
                    () => new CustomResult(new BoolResult(error), error))
                .TraceErrorsOnlyIfEnabled(enableTracing: true)
                .Run(caller: "failure");
            result.Succeeded.Should().BeFalse();

            // The output should have an error
            fullOutput = GetFullOutput();
            fullOutput.Should().Contain("failure");
            fullOutput.Should().Contain(error);

            // Running an operation that fails another time, but this time the tracing is off
            error = "My Error2";
            result = context.CreateOperation(
                    tracer,
                    () => new CustomResult(new BoolResult(error), error))
                .TraceErrorsOnlyIfEnabled(enableTracing: true)
                .Run(caller: "failure2");
            result.Succeeded.Should().BeFalse();

            // The error should not be in the output
            fullOutput = GetFullOutput();
            fullOutput.Should().Contain("failure2");
            fullOutput.Should().Contain(error);
        }

        [Fact]
        public void TraceOperationStartedEmitsComponentAndOperation()
        {
            var tracer = new Tracer("MyTracer");
            var mock = new StructuredLoggerMock();
            var context = new OperationContext(new Context(mock));

            // Running a successful operation first
            var result = context.CreateOperation(
                    tracer,
                    () => new CustomResult())
                .Run(caller: "success");

            mock.LogOperationStartedArgument.OperationName.Should().Be("success");
            mock.LogOperationStartedArgument.TracerName.Should().Be("MyTracer");
        }

        private class StructuredLoggerMock : IStructuredLogger
        {
            public void Dispose()
            {
            }

            public Severity CurrentSeverity { get; }

            public int ErrorCount { get; }

            public void Flush()
            {
            }

            public void Always(string messageFormat, params object[] messageArgs)
            {
            }

            public void Fatal(string messageFormat, params object[] messageArgs)
            {
            }

            public void Error(string messageFormat, params object[] messageArgs)
            {
            }

            public void Error(Exception exception, string messageFormat, params object[] messageArgs)
            {
            }

            public void ErrorThrow(Exception exception, string messageFormat, params object[] messageArgs)
            {
            }

            public void Warning(string messageFormat, params object[] messageArgs)
            {
            }

            public void Info(string messageFormat, params object[] messageArgs)
            {
            }

            public void Debug(string messageFormat, params object[] messageArgs)
            {
            }

            public void Debug(Exception exception)
            {
            }

            public void Diagnostic(string messageFormat, params object[] messageArgs)
            {
            }

            public void Log(Severity severity, string message)
            {
            }

            public void LogFormat(Severity severity, string messageFormat, params object[] messageArgs)
            {
            }

            public void Log(Severity severity, string correlationId, string message)
            {
            }

            public void Log(in LogMessage logMessage)
            {
            }

            public OperationStarted LogOperationStartedArgument;

            public void LogOperationStarted(in OperationStarted operation)
            {
                LogOperationStartedArgument = operation;
            }

            public void LogOperationFinished(in OperationResult result)
            {
            }
        }


        private class CustomResult : BoolResult
        {
            public CustomResult() { }

            public CustomResult(ResultBase other, string message)
                : base(other, message)
            { }
        }

        private Exception GetException()
        {
            try
            {
                local();
                throw null;
            }
            catch (InvalidOperationException e)
            {
                return e;
            }

            void local() => throw new InvalidOperationException("Message");
        }
    }
}
