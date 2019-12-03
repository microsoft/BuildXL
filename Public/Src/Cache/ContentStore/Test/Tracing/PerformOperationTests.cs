using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
