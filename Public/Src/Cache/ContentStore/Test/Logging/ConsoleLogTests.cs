// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Logging
{
    public class ConsoleLogTests : TestBase
    {
        /// <summary>
        /// Inspectable logger intercepts all the messages that go into a console and prints them in ToString method.
        /// This makes the logger testable and allows us to run them in parallel with other tests that may change the output of the console.
        /// </summary>
        private class InspectableConsoleLog : ConsoleLog
        {
            private readonly StringBuilder _sb = new StringBuilder();

            /// <inheritdoc />
            public InspectableConsoleLog(Severity severity = Severity.Diagnostic, bool useShortLayout = true, bool printSeverity = false)
                : base(severity, useShortLayout, printSeverity)
            {
            }

            /// <inheritdoc />
            protected override void WriteError(string line) => _sb.AppendLine(line);

            /// <inheritdoc />
            protected override void WriteLine(string line) => _sb.AppendLine(line);

            public override string ToString() => _sb.ToString();
        }

        public ConsoleLogTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public void AlwaysMethodSucceeds()
        {
            WriteAndVerify(Severity.Always);
        }

        [Fact]
        public void FatalMethodSucceeds()
        {
            WriteAndVerify(Severity.Fatal);
        }

        [Fact]
        public void ErrorMethodSucceeds()
        {
            WriteAndVerify(Severity.Error);
        }

        [Fact]
        public void WarningMethodSucceeds()
        {
            WriteAndVerify(Severity.Warning);
        }

        [Fact]
        public void InfoMethodSucceeds()
        {
            WriteAndVerify(Severity.Info);
        }

        [Fact]
        public void DebugMethodSucceeds()
        {
            WriteAndVerify(Severity.Debug);
        }

        [Fact]
        public void DiagnosticMethodSucceeds()
        {
            WriteAndVerify(Severity.Diagnostic);
        }

        [Fact]
        public void LongLayoutSucceeds()
        {
            using (var log = new InspectableConsoleLog(Severity.Debug, false))
            {
                WriteAndVerify(log, Severity.Debug);
            }
        }

        [Fact]
        public void FilteredSeverityIgnored()
        {
            using (var log = new InspectableConsoleLog(Severity.Debug))
            {
                WriteAndVerify(log, Severity.Unknown, true);
            }
        }

        private static void WriteAndVerify(Severity severity)
        {
            using (var log = new InspectableConsoleLog(severity))
            {
                WriteAndVerify(log, severity);
            }
        }

        private static void WriteAndVerify(ILog log, Severity severity, bool expectEmpty = false)
        {
            const string message = "message";
            log.Write(DateTime.Now, Thread.CurrentThread.ManagedThreadId, severity, message);

            if (!expectEmpty)
            {
                log.ToString().Should().Contain(message);
            }
        }
    }
}
