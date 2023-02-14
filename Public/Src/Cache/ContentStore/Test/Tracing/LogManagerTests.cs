// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.Host.Configuration;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.Test.Tracing
{
    public class LogManagerTests : TestWithOutput
    {
        public LogManagerTests(ITestOutputHelper output)
        : base(output)
        {
        }

        [Fact]
        public void TraceIfConfigurationIsMissing()
        {
            var tracer = new Tracer("MyTracer");
            var context = new Context(TestGlobal.Logger);
            var message = Guid.NewGuid().ToString();
            tracer.Debug(context, message);
            var output = GetFullOutput();
            output.Should().Contain(output);
        }

        [Fact]
        public void TraceIfNotFiltered()
        {
            var verboseOperationLogging = new OperationLoggingConfiguration()
                                          {
                                              ErrorsOnly = true,
                                          };
            var tracer = new Tracer(
                "MyTracer",
                new LogManager().Update(
                    new LogManagerConfiguration()
                    {
                        Logs = new Dictionary<string, OperationLoggingConfiguration>() {["MyTracer.*"] = verboseOperationLogging}
                    }));

            var context = new Context(TestGlobal.Logger);
            var message = Guid.NewGuid().ToString();

            tracer.Debug(context, message);
            var output = GetFullOutput();
            output.Should().BeEmpty();

            tracer.Error(context, message);
            output = GetFullOutput();
            output.Should().Contain(output);
        }

        [Fact]
        public void NotTraceIfFilteredByOperation()
        {
            var verboseOperationLogging = new OperationLoggingConfiguration()
            {
                Disabled = true,
            };
            var tracer = new Tracer(
                "MyTracer",
                new LogManager().Update(
                    new LogManagerConfiguration()
                    {
                        Logs = new Dictionary<string, OperationLoggingConfiguration>() { ["MyTracer.MyOp"] = verboseOperationLogging }
                    }));

            var context = new Context(TestGlobal.Logger);
            var message = Guid.NewGuid().ToString();

            // Only 'MyOp' is disabled.
            tracer.Debug(context, message, operation: "MyOp");
            var output = GetFullOutput();
            output.Should().BeEmpty();

            // Only 'MyOp' is disabled. The next one should work just fine.
            tracer.Debug(context, message, operation: "SomeOperation");
            output = GetFullOutput();
            output.Should().Contain(message);
        }

        [Fact]
        public void NotTraceIfDisabled()
        {
            var verboseOperationLogging = new OperationLoggingConfiguration()
            {
                Disabled = true,
            };
            var tracer = new Tracer(
                "MyTracer",
                new LogManager().Update(
                    new LogManagerConfiguration()
                    {
                        Logs = new Dictionary<string, OperationLoggingConfiguration>() { ["MyTracer.*"] = verboseOperationLogging }
                    }));

            var context = new Context(TestGlobal.Logger);
            var message = Guid.NewGuid().ToString();

            // Only 'MyOp' is disabled.
            tracer.Debug(context, message);
            var output = GetFullOutput();
            output.Should().BeEmpty();

            tracer.Error(context, message);
            output = GetFullOutput();
            output.Should().BeEmpty();

            tracer.Always(context, message);
            output = GetFullOutput();
            output.Should().BeEmpty();
        }

        [Fact]
        public void NotTraceIfFilteredIfNotError()
        {
            var verboseOperationLogging = new OperationLoggingConfiguration()
                                          {
                                              ErrorsOnly = true,
                                          };
            var tracer = new Tracer(
                "MyTracer",
                new LogManager().Update(
                    new LogManagerConfiguration()
                    {
                        Logs = new Dictionary<string, OperationLoggingConfiguration>() {["MyTracer.*"] = verboseOperationLogging}
                    }));

            var context = new Context(TestGlobal.Logger);
            var message = Guid.NewGuid().ToString();

            tracer.Debug(context, message);
            var output = GetFullOutput();
            output.Should().BeEmpty();

            tracer.Error(context, message);
            output = GetFullOutput();
            output.Should().Contain(output);
        }
    }
}
