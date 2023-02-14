// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Utilities.Tasks;
using FluentAssertions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;
using Exception = System.Exception;

namespace BuildXL.Cache.ContentStore.Test.Tracing
{
    public class PerformOperationTests : TestWithOutput
    {
        public PerformOperationTests(ITestOutputHelper output)
        : base(output)
        {
        }

        public enum AsyncOperationKind
        {
            Initialization,
            AsyncOperation,
            AsyncOperationWithTimeout,
        }

        [Theory]
        [InlineData(AsyncOperationKind.Initialization)]
        [InlineData(AsyncOperationKind.AsyncOperation)]
        [InlineData(AsyncOperationKind.AsyncOperationWithTimeout)]
        public async Task AsyncOperationShouldNotBeExecutedIfCancellationTokenIsSet(AsyncOperationKind kind)
        {
            var tracer = new Tracer("MyTracer");
            var cts = new CancellationTokenSource();
            var context = new OperationContext(new Context(TestGlobal.Logger), cts.Token);

            cts.Cancel();
            bool callbackIsCalled = false;

            Func<Task<BoolResult>> operation = async () =>
                                               {
                                                   callbackIsCalled = true;
                                                   await Task.Delay(TimeSpan.FromSeconds(1));
                                                   return BoolResult.Success;
                                               };

            Task<BoolResult> resultTask = kind switch
            {
                AsyncOperationKind.AsyncOperation => context.PerformOperationAsync(tracer, operation),
                AsyncOperationKind.AsyncOperationWithTimeout => context.PerformOperationWithTimeoutAsync(tracer, _ => operation(), timeout: TimeSpan.MaxValue),
                AsyncOperationKind.Initialization => context.PerformInitializationAsync(tracer, operation),
                _ => throw new InvalidOperationException(),
            };

            var r = await resultTask;

            callbackIsCalled.Should().BeFalse();
            r.ShouldBeError();
            r.IsCancelled.Should().BeTrue();
        }

        [Fact]
        public async Task NonResultOperationShouldNotBeExecutedIfCancellationTokenIsSet()
        {
            var tracer = new Tracer("MyTracer");
            var cts = new CancellationTokenSource();
            var context = new OperationContext(new Context(TestGlobal.Logger), cts.Token);

            cts.Cancel();
            bool callbackIsCalled = false;
            var r = context.PerformNonResultOperationAsync(
                tracer,
                async () =>
                {
                    callbackIsCalled = true;
                    await Task.Yield();
                    return BoolResult.Success;
                });
            await Assert.ThrowsAsync<OperationCanceledException>(() => r);
            callbackIsCalled.Should().BeFalse();
        }

        [Fact]
        public void TestResultPropagationException()
        {
            const string errorMessage = "Invalid operation error";
            var tracer = new Tracer("MyTracer");
            var context = new OperationContext(new Context(TestGlobal.Logger));

            var r1 = context.PerformOperation(
                tracer,
                () =>
                {
                    new BoolResult(exceptionWithStackTrace()).ThrowIfFailure();
                    return BoolResult.Success;

                    Exception exceptionWithStackTrace()
                    {
                        try
                        {
                            nested();
                            throw new InvalidOperationException("Should not get here!");
                        }
                        catch (Exception e)
                        {
                            return e;
                        }
                    }

                    void nested() => throw new InvalidOperationException(errorMessage);
                });

            var r2 = context.PerformOperation(
                tracer,
                () =>
                {
                    r1.ThrowIfFailure();
                    return BoolResult.Success;
                });

            var r3 = context.PerformOperation(
                tracer,
                () =>
                {
                    r2.ThrowIfFailure();
                    return BoolResult.Success;
                }, extraStartMessage: "Final operation");

            var finalErrorResult = r3.ToString();

            // The final result should contain an original method that threw the error.
            finalErrorResult.Should().Contain("nested");
            finalErrorResult.Should().NotContain(nameof(Interfaces.Results.ResultsExtensions.ThrowIfFailure));

            // Both the full string and the error message should have the original error message
            finalErrorResult.Should().Contain(errorMessage);
            r3.ErrorMessage.Should().Contain(errorMessage);

            // The error message should not be repeated multiple times.
            finalErrorResult.Split(new string[] { errorMessage }, StringSplitOptions.RemoveEmptyEntries).Length.Should().Be(2);
        }

        [Fact]
        public async Task TestResultPropagationExceptionInAsyncMethod()
        {
            const string errorMessage = "Invalid operation error";
            var tracer = new Tracer("MyTracer");
            var context = new OperationContext(new Context(TestGlobal.Logger));

            var r1 = context.PerformOperationAsync(
                tracer,
                () =>
                {
                    new BoolResult(exceptionWithStackTrace()).ThrowIfFailure();
                    return BoolResult.SuccessTask;

                    Exception exceptionWithStackTrace()
                    {
                        try
                        {
                            nested();
                            throw new InvalidOperationException("Should not get here!");
                        }
                        catch (Exception e)
                        {
                            return e;
                        }
                    }

                    void nested() => throw new InvalidOperationException(errorMessage);
                });

            var r3 = await context.PerformOperationAsync(
                tracer,
                async () =>
                {
                    await r1.ThrowIfFailureAsync();
                    return BoolResult.Success;
                }, extraStartMessage: "Final operation");

            var finalErrorResult = r3.ToString();

            Output.WriteLine("R1: " + (await r1));

            // The final result should contain an original method that threw the error.
            finalErrorResult.Should().Contain("nested");
            finalErrorResult.Should().NotContain(nameof(Interfaces.Results.ResultsExtensions.ThrowIfFailureAsync));

            // Both the full string and the error message should have the original error message
            finalErrorResult.Should().Contain(errorMessage);
            r3.ErrorMessage.Should().Contain(errorMessage);

            // The error message should not be repeated multiple times.
            finalErrorResult.Split(new string[] { errorMessage }, StringSplitOptions.RemoveEmptyEntries).Length.Should().Be(2);
        }

        [Fact]
        public void OperationShouldNotBeExecutedIfCancellationTokenIsSet()
        {
            var tracer = new Tracer("MyTracer");
            var cts = new CancellationTokenSource();
            var context = new OperationContext(new Context(TestGlobal.Logger), cts.Token);

            cts.Cancel();
            bool callbackIsCalled = false;
            var r = context.PerformOperation(
                tracer,
                () =>
                {
                    callbackIsCalled = true;
                    return BoolResult.Success;
                });

            callbackIsCalled.Should().BeFalse();
            r.ShouldBeError();
            r.IsCancelled.Should().BeTrue();
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
            fullOutput.Should().Contain("not finished yet");
        }

        [Fact]
        public async Task PassingTimeSpanMaxShouldWork()
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
                pendingOperationTracingInterval: TimeSpan.MaxValue);
            r.ShouldBeSuccess();
        }

        [Fact]
        public async Task PassingInfiniteTimeSpanShouldWork()
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
                pendingOperationTracingInterval: Timeout.InfiniteTimeSpan);
            r.ShouldBeSuccess();
        }

        [Fact]
        public async Task OperationNameIsOriginalWhenTracedPeriodically()
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
            fullOutput.Should().NotContain("MyTracer.TracePendingOperation:");
        }

        private class LongStartup : StartupShutdownSlimBase
        {
            private readonly TimeSpan _startupDuration;

            /// <inheritdoc />
            public LongStartup(TimeSpan startupDuration)
            {
                _startupDuration = startupDuration;
            }

            /// <inheritdoc />
            protected override Tracer Tracer => new Tracer(nameof(LongStartup));

            /// <inheritdoc />
            protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
            {
                await Task.Delay(_startupDuration);
                return await base.StartupCoreAsync(context);
            }
        }

        [Fact]
        public async Task OperationNameIsCorrectForPendingStartupAsync()
        {
            // This test checks that when the startup takes a long time, the operation name is propagated properly
            // to the long operation tracer.

            // This test relies on static state. We'll have issues if more then one test will start changing the log manager configuration!
            LogManager.Instance.Update(
                new LogManagerConfiguration()
                {
                    Logs = new Dictionary<string, OperationLoggingConfiguration>()
                           {
                               ["LongStartup.StartupAsync"] = new OperationLoggingConfiguration()
                                                              {
                                                                  ErrorsOnly = false, StartMessage = true, PendingOperationTracingInterval = "100ms"
                                                              }
                           }
                });
            var context = new OperationContext(new Context(TestGlobal.Logger));
            var component = new LongStartup(TimeSpan.FromSeconds(1));
            var r = await component.StartupAsync(context);
            r.ShouldBeSuccess();

            var fullOutput = GetFullOutput();
            
            fullOutput.Should().Contain("'LongStartup.StartupAsync' has been running");
        }

        [Fact]
        public async Task OperationNameIsCorrectForPendingOperationsCreatedWithOptions()
        {
            // This test case checks that the 'OperationName' is set correctly even with 'WithOptions' call does not pass it through.
            var tracer = new Tracer("MyTracer");
            var context = new OperationContext(new Context(TestGlobal.Logger));

            // WithOptions takes a 'caller' argument and in some cases that argument can be missing.
            // But RunAsync takes a caller as well and if 'WithOptions' didn't set the 'Caller' field, that field should be updated
            // by RunAsync.
            // Note: the Caller field is used only for tracing pending operations.
            var r = await context.CreateOperation(
                tracer,
                async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    return BoolResult.Success;
                })
                .WithOptions(pendingOperationTracingInterval: TimeSpan.FromMilliseconds(10))
                .RunAsync("TheOperationName");
            r.ShouldBeSuccess();

            var fullOutput = GetFullOutput();
            fullOutput.Should().NotContain("'MyTracer.'");
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
                fullOutput.Should().Contain("not finished yet");
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
        public async Task TimedOutOperationHasOperationNameAndNotStackTrace()
        {
            var tracer = new Tracer("MyTracer");
            var context = new OperationContext(new Context(TestGlobal.Logger));

            int longOperationDurationMs = 10_000;
            var timeout = TimeSpan.FromMilliseconds(longOperationDurationMs / 100);
            var result = await context.PerformOperationWithTimeoutAsync(
                tracer,
                _ => operation(longOperationDurationMs),
                timeout: timeout,
                caller: "MyOperation");

            result.ShouldBeError();
            // The operation should fail gracefully without exception messages and stacktraces.
            result.Exception.Should().BeOfType<TimeoutException>();
            result.ToString().Should().Contain("TimeoutException");
            result.ToString().Should().Contain("The operation 'MyOperation' has timed out");
            
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

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EndMessageFactoryBehaviorForSuccessfulCase(bool theCallbackShouldBeCalled)
        {
            // In prod the tracing level is Debug, so the message factory is not called.

            // The callback should be called for Diagnostics severity but not for the higher severities.
            Severity severity = theCallbackShouldBeCalled ? Severity.Diagnostic : Severity.Debug;
            var tracer = new Tracer("MyTracer");
            var context = new OperationContext(new Context(new TestGlobal.TestLogger(new NullLog(severity))));
            bool endMessageFactoryWasCalled = false;
            var result = context.CreateOperation(
                    tracer,
                    () =>
                    {
                        return new Result<int>(42);
                    })
                .WithOptions(endMessageFactory: r =>
                                                {
                                                    endMessageFactoryWasCalled = true;
                                                    return r.Succeeded ? "ExtraSuccess" : "ExtraFailure";
                                                }, traceErrorsOnly: true)
                .Run();

            endMessageFactoryWasCalled.Should().Be(theCallbackShouldBeCalled);
        }

        [Fact]
        public void FailureIsNotCriticalIfCanceled()
        {
            var tracer = new Tracer("MyTracer");
            var cts = new CancellationTokenSource();
            var context = new OperationContext(new Context(TestGlobal.Logger), cts.Token);
            cts.Cancel();

            var result = context.CreateOperation(
                    tracer,
                    () =>
                    {
                        context.Token.ThrowIfCancellationRequested();
                        return BoolResult.Success;
                    })
                .WithOptions(traceOperationFinished: true, isCritical: true)
                .Run();

            result.IsCancelled.Should().BeTrue();
            result.IsCriticalFailure.Should().BeFalse();
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
