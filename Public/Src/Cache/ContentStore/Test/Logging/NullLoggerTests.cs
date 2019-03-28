// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Logging
{
    public class NullLoggerTests : TestBase
    {
        private const string Message = "message";
        private const string MessageFormat = "argument {0}";
        private const int Arg = 1;

        public NullLoggerTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public void MethodsTakeMessageString()
        {
            var logger = NullLogger.Instance;
            logger.Fatal(Message);
            logger.Error(Message);
            logger.Warning(Message);
            logger.Always(Message);
            logger.Info(Message);
            logger.Debug(Message);
            logger.Diagnostic(Message);
        }

        [Fact]
        public void LogMessage()
        {
            var logger = NullLogger.Instance;
            logger.Log(Severity.Diagnostic, Message);
            logger.Log(Severity.Debug, Message);
            logger.Log(Severity.Info, Message);
            logger.Log(Severity.Warning, Message);
            logger.Log(Severity.Error, Message);
            logger.Log(Severity.Fatal, Message);
            logger.Log(Severity.Always, Message);
        }

        [Fact]
        public void LogFormat()
        {
            var logger = NullLogger.Instance;
            logger.LogFormat(Severity.Diagnostic, Message);
            logger.LogFormat(Severity.Debug, Message);
            logger.LogFormat(Severity.Info, Message);
            logger.LogFormat(Severity.Warning, Message);
            logger.LogFormat(Severity.Error, Message);
            logger.LogFormat(Severity.Fatal, Message);
            logger.LogFormat(Severity.Always, Message);
        }

        [Fact]
        public void MethodsTakeMessageFormatString()
        {
            var logger = NullLogger.Instance;
            logger.Fatal(MessageFormat, Arg);
            logger.Error(MessageFormat, Arg);
            logger.Warning(MessageFormat, Arg);
            logger.Always(MessageFormat, Arg);
            logger.Info(MessageFormat, Arg);
            logger.Debug(MessageFormat, Arg);
            logger.Diagnostic(MessageFormat, Arg);
        }

        [Fact]
        public void MethodsTakeExceptionAndMessageFormatString()
        {
            var exception = new InvalidOperationException();
            NullLogger.Instance.Error(exception, MessageFormat, Arg);
        }

        [Fact]
        public void MethodsTakeException()
        {
            var exception = new InvalidOperationException();
            NullLogger.Instance.Debug(exception);
        }

        [Fact]
        public void ErrorThrowMethodThrows()
        {
            Action a = () => NullLogger.Instance.ErrorThrow(new InvalidOperationException(), MessageFormat, Arg);
            a.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void LogMethodTakesSeverity()
        {
            NullLogger.Instance.Log(Severity.Fatal, Message);
            NullLogger.Instance.LogFormat(Severity.Fatal, MessageFormat, Arg);
        }

        [Fact]
        public void CurrentSeverityGivesLowest()
        {
            var logger = NullLogger.Instance as ILogger;
            logger.CurrentSeverity.Should().Be(Severity.Diagnostic);
        }
    }
}
