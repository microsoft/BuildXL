using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
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
