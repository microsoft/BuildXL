// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Threading;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Logging
{
    public class LoggerTests : TestBase
    {
        private const string Message = "message";
        private const string MessageFormat = "argument {0}";
        private const int Arg = 1;
        private const string FormattedMessage = "argument 1";

        private const string ExceptionalFormattedMessage =
            "argument 1, Exception=[System.InvalidOperationException: text]";

        private readonly Exception _exception = new InvalidOperationException("text");

        private class LogWriteArgs
        {
            public int ThreadId;
            public Severity Severity;
            public string MessageString;
        }

        private class TestLog : ILog
        {
            public Severity TestSeverity { get; set; }
            public bool Flushed { get; private set; } = false;
            public LogWriteArgs LogWriteArgs { get; private set; } = null;

            public Severity CurrentSeverity => TestSeverity;
            
            public void Dispose()
            {
            }

            public void Flush()
            {
                Flushed = true;
            }

            public void Write(DateTime dateTime, int threadId, Severity severity, string message)
            {
                LogWriteArgs = new LogWriteArgs()
                {
                    ThreadId = threadId,
                    Severity = severity,
                    MessageString = message
                };
            }
        }

        public LoggerTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public void CurrentSeverityGivesLowest()
        {
            using (var logger = new Logger())
            {
                var mockLog1 = new TestLog();
                var mockLog2 = new TestLog();
                mockLog1.TestSeverity = Severity.Error;
                mockLog2.TestSeverity = Severity.Debug;
                logger.AddLog(mockLog1);
                logger.AddLog(mockLog2);

                logger.CurrentSeverity.Should().Be(Severity.Debug);
            }
        }

        [Fact]
        public void FlushesLogsSynchronously()
        {
            var mockLog = new TestLog();

            using (var logger = new Logger(true, mockLog))
            {
                logger.Flush();
                mockLog.Flushed.Should().BeTrue();
            }
        }

        [Fact]
        public void FlushesLogsAsynchronously()
        {
            var mockLog = new TestLog();

            using (var logger = new Logger(mockLog))
            {
                logger.Flush();
            }

            mockLog.Flushed.Should().BeTrue();
        }

        [Fact(Skip = "Enable tests or remove them.")]
        [Trait("Category", "QTestSkip")] // Skipped
        public void FlushesLogsOccasionally()
        {
            var mockLog = new TestLog();

            var flushInterval = TimeSpan.FromSeconds(.1);
            var timeout = TimeSpan.FromSeconds(5);
            var stopwatch = Stopwatch.StartNew();
            using (new Logger(flushInterval, mockLog))
            {
                while (!mockLog.Flushed)
                {
                    stopwatch.Elapsed.Should().BeLessOrEqualTo(timeout);
                    Thread.Sleep(flushInterval);
                }
            }
        }

        [Fact]
        public void ErrorCountIncremented()
        {
            using (ILogger logger = new Logger())
            {
                logger.Error(Message);
                logger.ErrorCount.Should().Be(1);
            }
        }

        [Fact]
        public void FatalMethodWritesCorrectSeverity()
        {
            InvokeLoggerMethod(logger => logger.Fatal(Message)).Severity.Should().Be(Severity.Fatal);
        }

        [Fact]
        public void FatalMethodWritesCorrectMessage()
        {
            InvokeLoggerMethod(logger => logger.Fatal(MessageFormat, Arg)).MessageString.Should().Be(FormattedMessage);
        }

        [Fact]
        public void ErrorMethodWritesCorrectSeverity()
        {
            InvokeLoggerMethod(logger => logger.Error(Message)).Severity.Should().Be(Severity.Error);
            InvokeLoggerMethod(logger => logger.Error(_exception, Message)).Severity.Should().Be(Severity.Error);
        }

        [Fact]
        public void ErrorMethodWritesCorrectMessage()
        {
            InvokeLoggerMethod(logger => logger.Error(MessageFormat, Arg)).MessageString.Should().Be(FormattedMessage);
        }

        [Fact]
        public void ErrorMethodWritesCorrectExceptionalMessage()
        {
            InvokeLoggerMethod(logger => logger.Error(_exception, MessageFormat, Arg))
                .MessageString.Should()
                .Be(ExceptionalFormattedMessage);
        }

        [Fact]
        public void ErrorThrowMethodThrows()
        {
            using (var logger = new Logger() as ILogger)
            {
                Action a = () => logger.ErrorThrow(_exception, MessageFormat, Arg);
                a.Should().Throw<InvalidOperationException>();
            }
        }

        [Fact]
        public void ErrorThrowFailsContractGivenNullException()
        {
            using (var logger = new Logger())
            {
                Action a = () => logger.ErrorThrow(null, MessageFormat, Arg);
                a.Should().Throw<Exception>();

                Action b = () => logger.ErrorThrow(_exception, null, Arg);
                b.Should().Throw<Exception>();

                Action c = () => logger.ErrorThrow(null, null, Arg);
                c.Should().Throw<Exception>();
            }
        }

        [Fact]
        public void WarnMethodWritesCorrectSeverity()
        {
            InvokeLoggerMethod(logger => logger.Warning(Message)).Severity.Should().Be(Severity.Warning);
        }

        [Fact]
        public void WarnMethodWritesCorrectMessage()
        {
            InvokeLoggerMethod(logger => logger.Warning(MessageFormat, Arg)).MessageString.Should().Be(FormattedMessage);
        }

        [Fact]
        public void NormalMethodWritesCorrectSeverity()
        {
            InvokeLoggerMethod(logger => logger.Always(Message)).Severity.Should().Be(Severity.Always);
        }

        [Fact]
        public void NormalMethodWritesCorrectMessage()
        {
            InvokeLoggerMethod(logger => logger.Always(MessageFormat, Arg)).MessageString.Should().Be(FormattedMessage);
        }

        [Fact]
        public void InfoMethodWritesCorrectSeverity()
        {
            InvokeLoggerMethod(logger => logger.Info(Message)).Severity.Should().Be(Severity.Info);
        }

        [Fact]
        public void InfoMethodWritesCorrectMessage()
        {
            InvokeLoggerMethod(logger => logger.Info(MessageFormat, Arg)).MessageString.Should().Be(FormattedMessage);
        }

        [Fact]
        public void DebugMethodWritesCorrectSeverity()
        {
            InvokeLoggerMethod(logger => logger.Debug(Message)).Severity.Should().Be(Severity.Debug);
        }

        [Fact]
        public void DebugMethodWritesCorrectMessage()
        {
            InvokeLoggerMethod(logger => logger.Debug(MessageFormat, Arg)).MessageString.Should().Be(FormattedMessage);
        }

        [Fact]
        public void DebugMethodWritesCorrectExceptionalMessage()
        {
            InvokeLoggerMethod(logger => logger.Debug(_exception))
                .MessageString.Should().Be("System.InvalidOperationException: text");
        }

        [Fact]
        public void DiagnosticMethodWritesCorrectSeverity()
        {
            InvokeLoggerMethod(logger => logger.Diagnostic(Message)).Severity.Should().Be(Severity.Diagnostic);
        }

        [Fact]
        public void DiagnosticMethodWritesCorrectMessage()
        {
            InvokeLoggerMethod(logger => logger.Diagnostic(MessageFormat, Arg))
                .MessageString.Should()
                .Be(FormattedMessage);
        }

        [Fact]
        public void LogMethodWritesCorrectSeverity()
        {
            InvokeLoggerMethod(logger => logger.Log(Severity.Diagnostic, Message))
                .Severity.Should()
                .Be(Severity.Diagnostic);
        }

        [Fact]
        public void LogMethodWritesCorrectMessage()
        {
            InvokeLoggerMethod(logger => logger.Log(Severity.Diagnostic, Message)).MessageString.Should().Be(Message);
        }

        [Fact]
        public void LogFormatMethodWritesCorrectSeverity()
        {
            InvokeLoggerMethod(logger => logger.LogFormat(Severity.Diagnostic, MessageFormat, Arg))
                .Severity.Should()
                .Be(Severity.Diagnostic);
        }

        [Fact]
        public void LogFormatMethodWritesCorrectMessage()
        {
            InvokeLoggerMethod(logger => logger.LogFormat(Severity.Diagnostic, MessageFormat, Arg))
                .MessageString.Should()
                .Be(FormattedMessage);
        }

        private static LogWriteArgs InvokeLoggerMethod(Action<ILogger> action)
        {
            var mockLog = new TestLog();

            using (var logger = new Logger(true, mockLog))
            {
                action(logger);
                mockLog.LogWriteArgs.Should().NotBeNull();
            }

            mockLog.LogWriteArgs.ThreadId.Should().Be(Thread.CurrentThread.ManagedThreadId);

            return mockLog.LogWriteArgs;
        }

        [Fact]
        public void WritesAsynchronously()
        {
            var mockLog = new TestLog();

            using (var logger = new Logger(mockLog))
            {
                logger.Always(Message);
            }

            mockLog.LogWriteArgs.Should().NotBeNull();
        }
    }
}
