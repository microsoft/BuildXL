// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using ContentStoreTest.Test;
#if DEBUG
using FluentAssertions;
#endif
using Xunit;

namespace ContentStoreTest.Logging
{
    public class DebugPrintLogTests : TestBase
    {
        private static readonly object Lock = new object();

        public DebugPrintLogTests()
            : base(TestGlobal.Logger)
        {
        }
#if NET_FRAMEWORK
        [Fact]
        public void FatalMethodSucceeds()
        {
            using (var log = new DebugPrintLog(Severity.Fatal))
            {
                WriteAndVerify(log, Severity.Fatal);
            }
        }

        [Fact]
        public void ErrorMethodSucceeds()
        {
            using (var log = new DebugPrintLog(Severity.Error))
            {
                WriteAndVerify(log, Severity.Error);
            }
        }

        [Fact]
        public void WarningMethodSucceeds()
        {
            using (var log = new DebugPrintLog(Severity.Warning))
            {
                WriteAndVerify(log, Severity.Warning);
            }
        }

        [Fact]
        public void InfoMethodSucceeds()
        {
            using (var log = new DebugPrintLog(Severity.Info))
            {
                WriteAndVerify(log, Severity.Info);
            }
        }

        [Fact]
        public void NormalMethodSucceeds()
        {
            using (var log = new DebugPrintLog(Severity.Always))
            {
                WriteAndVerify(log, Severity.Always);
            }
        }

        [Fact]
        public void DebugMethodSucceeds()
        {
            using (var log = new DebugPrintLog(Severity.Debug))
            {
                WriteAndVerify(log, Severity.Debug);
            }
        }

        [Fact]
        public void DiagnosticMethodSucceeds()
        {
            using (var log = new DebugPrintLog(Severity.Diagnostic))
            {
                WriteAndVerify(log, Severity.Diagnostic);
            }
        }

        [Fact]
        public void InvalidSeverityIgnored()
        {
            using (var log = new DebugPrintLog(Severity.Debug))
            {
                WriteAndVerify(log, Severity.Unknown, true);
            }
        }


        private static void WriteAndVerify(ILog log, Severity logSeverity, bool expectEmpty = false)
        {
            lock (Lock)
            {
                using (var stringWriter = new StringWriter(CultureInfo.CurrentCulture))
                {
                    const string message = "message";
                    var index = -1;
                    try
                    {
                        index = Debug.Listeners.Add(new TextWriterTraceListener(stringWriter));
                        log.Write(DateTime.Now, Thread.CurrentThread.ManagedThreadId, logSeverity, message);
                    }
                    finally
                    {
                        if (index >= 0)
                        {
                            Debug.Listeners.RemoveAt(index);
                        }
                    }

                    if (!expectEmpty)
                    {
#if DEBUG
                        stringWriter.ToString().Should().Contain(message);
#endif
                    }
                }
            }
        }
#endif
    }
}
