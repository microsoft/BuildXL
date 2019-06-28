// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
#if !FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using System.Diagnostics.Tracing;
#else
using Microsoft.Diagnostics.Tracing;
#endif

namespace Test.BuildXL.Utilities
{
    public sealed class EventListenerTests
    {
        private const int InfoEventId = 10001;
        private const int WarningEventId = 10002;

        [Fact]
        public void BaseEventListener()
        {
            // make sure this thing only deals with the BuildXL event source
            using (var listener = new TrackingEventListener(Events.Log))
            {
                // this one should get through
                Events log = Events.Log;
                log.InfoEvent("Test 1");
                XAssert.AreEqual(listener.InformationalCount, 1);

                // this one should not
                using (var log2 = new TestEventSource())
                {
                    log2.InfoEvent("Test 1");
                    XAssert.AreEqual(listener.InformationalCount, 1);
                }
            }
        }

        [Fact]
        public void BaseEventListenerDiagnosticFiltering()
        {
            using (var listener = new TestEventListener(Events.Log, "Test.BuildXL.Utilities.EventListenerTests.BaseEventListenerDiagnosticFiltering", captureAllDiagnosticMessages: false))
            {
                // Initially, diagnostic events are disabled.
                Events.Log.DiagnosticEvent("Super low level");
                Events.Log.DiagnosticEventInOtherTask("Also super low level");
                XAssert.AreEqual(0, listener.GetEventCount(EventId.DiagnosticEvent));
                XAssert.AreEqual(0, listener.GetEventCount(EventId.DiagnosticEventInOtherTask));

                // We can enable messages from one task, but leave those in another disabled.
                listener.EnableTaskDiagnostics(Tasks.UnitTest);

                Events.Log.DiagnosticEvent("Super low level");
                Events.Log.DiagnosticEventInOtherTask("Also super low level");
                XAssert.AreEqual(1, listener.GetEventCount(EventId.DiagnosticEvent));
                XAssert.AreEqual(0, listener.GetEventCount(EventId.DiagnosticEventInOtherTask));
            }
        }

        [Fact]
        public void WarningMapping()
        {
            Events log = Events.Log;

            var wm = new WarningManager();

            using (var listener = new TrackingEventListener(Events.Log, warningMapper: wm.GetState))
            {
                // should log as a warning
                XAssert.AreEqual(0, listener.WarningCount);
                log.WarningEvent("1");
                XAssert.AreEqual(1, listener.WarningCount);
                XAssert.AreEqual(0, listener.TotalErrorCount);

                wm.SetState(WarningEventId, WarningState.AsError);

                // should log as an error
                log.WarningEvent("1");
                XAssert.AreEqual(1, listener.TotalErrorCount);
                XAssert.AreEqual(1, listener.WarningCount);

                wm.SetState(WarningEventId, WarningState.Suppressed);

                // should be suppressed
                log.WarningEvent("1");
                XAssert.AreEqual(1, listener.WarningCount);
                XAssert.AreEqual(1, listener.TotalErrorCount);
            }

            using (var listener = new TrackingEventListener(Events.Log, warningMapper: wm.GetState))
            {
                // should log as a info
                XAssert.AreEqual(0, listener.InformationalCount);
                log.InfoEvent("1");
                XAssert.AreEqual(1, listener.InformationalCount);
                XAssert.AreEqual(0, listener.WarningCount);
                XAssert.AreEqual(0, listener.TotalErrorCount);

                wm.SetState(InfoEventId, WarningState.AsError);

                // should not log as an error (only warnings are managed by warning mapper)
                log.InfoEvent("1");
                XAssert.AreEqual(2, listener.InformationalCount);
                XAssert.AreEqual(0, listener.TotalErrorCount);

                wm.SetState(InfoEventId, WarningState.Suppressed);

                // should not be suppressed (only warnings are managed by warning mapper)
                log.InfoEvent("1");
                XAssert.AreEqual(3, listener.InformationalCount);
                XAssert.AreEqual(0, listener.TotalErrorCount);
            }
        }

        [SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        [Fact]
        public void TextWriterEventListener()
        {
            string text;

            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                using (var listener = new TextWriterEventListener(Events.Log, writer, DateTime.UtcNow, warningNumber => WarningState.AsWarning, EventLevel.Warning))
                {
                    Events log = Events.Log;

                    // should be captured
                    log.AlwaysEvent("Cookie 1 ");
                    log.CriticalEvent("Cookie 2 ");
                    log.ErrorEvent("Cookie 3 ");
                    log.WarningEvent("Cookie 4 ");

                    // shouldn't be captured
                    log.InfoEvent("Cookie 5 ");
                    log.VerboseEvent("Cookie 6 ");
                }

                text = writer.ToString();
            }

            XAssert.IsTrue(Regex.IsMatch(text, ".*Cookie 1 .*"));
            XAssert.IsTrue(Regex.IsMatch(text, ".*Cookie 2 .*"));
            XAssert.IsTrue(Regex.IsMatch(text, ".*Cookie 3 .*"));
            XAssert.IsTrue(Regex.IsMatch(text, ".*Cookie 4 .*"));
            XAssert.IsFalse(Regex.IsMatch(text, ".*Cookie 5 .*"));
            XAssert.IsFalse(Regex.IsMatch(text, ".*Cookie 6 .*"));

            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                using (var listener = new TextWriterEventListener(Events.Log, writer, DateTime.UtcNow, warningNumber => WarningState.AsWarning))
                {
                    Events log = Events.Log;

                    // should be captured
                    log.AlwaysEvent("Cookie 11 ");
                    log.CriticalEvent("Cookie 12 ");
                    log.ErrorEvent("Cookie 13 ");
                    log.WarningEvent("Cookie 14 ");
                    log.InfoEvent("Cookie 15 ");
                    log.VerboseEvent("Cookie 16 ");
                }

                text = writer.ToString();
            }

            XAssert.IsTrue(Regex.IsMatch(text, ".*Cookie 11 .*"));
            XAssert.IsTrue(Regex.IsMatch(text, ".*Cookie 12 .*"));
            XAssert.IsTrue(Regex.IsMatch(text, ".*Cookie 13 .*"));
            XAssert.IsTrue(Regex.IsMatch(text, ".*Cookie 14 .*"));
            XAssert.IsTrue(Regex.IsMatch(text, ".*Cookie 15 .*"));
            XAssert.IsTrue(Regex.IsMatch(text, ".*Cookie 16 .*"));
        }

        [SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        [Fact]
        public void ErrorAndWarningListener()
        {
            string text;

            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                using (var listener = new ErrorAndWarningEventListener(Events.Log, writer, DateTime.UtcNow, true, false, warningNumber => WarningState.AsWarning))
                {
                    Events log = Events.Log;

                    // should be captured
                    log.CriticalEvent("Cookie 1 ");
                    log.ErrorEvent("Cookie 2 ");

                    // shouldn't be captured
                    log.WarningEvent("Cookie 3 ");
                    log.AlwaysEvent("Cookie 4 ");
                    log.InfoEvent("Cookie 5 ");
                    log.VerboseEvent("Cookie 6 ");
                }

                text = writer.ToString();
            }

            XAssert.IsTrue(Regex.IsMatch(text, ".*Cookie 1 .*"));
            XAssert.IsTrue(Regex.IsMatch(text, ".*Cookie 2 .*"));
            XAssert.IsFalse(Regex.IsMatch(text, ".*Cookie 3 .*"));
            XAssert.IsFalse(Regex.IsMatch(text, ".*Cookie 4 .*"));
            XAssert.IsFalse(Regex.IsMatch(text, ".*Cookie 5 .*"));
            XAssert.IsFalse(Regex.IsMatch(text, ".*Cookie 6 .*"));

            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                using (var listener = new ErrorAndWarningEventListener(Events.Log, writer, DateTime.UtcNow, false, true, warningNumber => WarningState.AsWarning))
                {
                    Events log = Events.Log;

                    // should be captured
                    log.WarningEvent("Cookie 3 ");

                    // shouldn't be captured
                    log.CriticalEvent("Cookie 1 ");
                    log.ErrorEvent("Cookie 2 ");
                    log.AlwaysEvent("Cookie 4 ");
                    log.InfoEvent("Cookie 5 ");
                    log.VerboseEvent("Cookie 6 ");
                }

                text = writer.ToString();
            }

            XAssert.IsFalse(Regex.IsMatch(text, ".*Cookie 1 .*"));
            XAssert.IsFalse(Regex.IsMatch(text, ".*Cookie 2 .*"));
            XAssert.IsTrue(Regex.IsMatch(text, ".*Cookie 3 .*"));
            XAssert.IsFalse(Regex.IsMatch(text, ".*Cookie 4 .*"));
            XAssert.IsFalse(Regex.IsMatch(text, ".*Cookie 5 .*"));
            XAssert.IsFalse(Regex.IsMatch(text, ".*Cookie 6 .*"));

            // repeat the same tests, but now suppress the warning message (i.e., emulate /noWarn:10002 (EventId.WarningEvent))
            // WarningMapper applies only to warning-level events, so we can check only that one
            // since the warning is being suppressed, nothing should be captured
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                using (var listener = new ErrorAndWarningEventListener(Events.Log, writer, DateTime.UtcNow, true, false, warningNumber => WarningState.Suppressed))
                {
                    Events log = Events.Log;

                    log.WarningEvent("Cookie 3 ");
                }

                text = writer.ToString();
            }
            
            XAssert.IsFalse(Regex.IsMatch(text, ".*Cookie 3 .*"));

            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                using (var listener = new ErrorAndWarningEventListener(Events.Log, writer, DateTime.UtcNow, false, true, warningNumber => WarningState.Suppressed))
                {
                    Events log = Events.Log;

                    log.WarningEvent("Cookie 3 ");
                }

                text = writer.ToString();
            }
            
            XAssert.IsFalse(Regex.IsMatch(text, ".*Cookie 3 .*"));
        }

        [Fact]
        public void TrackingEventListener()
        {
            const int NumVerbose = 9;
            const int NumInfo = 8;
            const int NumWarning = 7;
            const int NumError = 6;
            const int NumCritical = 5;
            const int NumAlways = 4;

            using (var listener = new TrackingEventListener(Events.Log))
            {
                Events log = Events.Log;

                for (int i = 0; i < NumVerbose; i++)
                {
                    log.VerboseEvent("1");
                }

                for (int i = 0; i < NumInfo; i++)
                {
                    log.InfoEvent("1");
                }

                for (int i = 0; i < NumWarning; i++)
                {
                    log.WarningEvent("1");
                }

                for (int i = 0; i < NumError; i++)
                {
                    log.ErrorEvent("1");
                }

                for (int i = 0; i < NumCritical; i++)
                {
                    log.CriticalEvent("1");
                }

                for (int i = 0; i < NumAlways; i++)
                {
                    log.AlwaysEvent("1");
                }

                XAssert.AreEqual(listener.VerboseCount, NumVerbose);
                XAssert.AreEqual(listener.InformationalCount, NumInfo);
                XAssert.AreEqual(listener.WarningCount, NumWarning);
                XAssert.AreEqual(listener.TotalErrorCount, NumError + NumCritical);
                XAssert.AreEqual(listener.CriticalCount, NumCritical);
                XAssert.AreEqual(listener.AlwaysCount, NumAlways);
                XAssert.IsTrue(listener.HasFailures);
                XAssert.IsTrue(listener.HasFailuresOrWarnings);
            }

            using (var listener = new TrackingEventListener(Events.Log))
            {
                Events log = Events.Log;

                XAssert.IsFalse(listener.HasFailures);
                XAssert.IsFalse(listener.HasFailuresOrWarnings);

                log.WarningEvent("1");

                XAssert.IsFalse(listener.HasFailures);
                XAssert.IsTrue(listener.HasFailuresOrWarnings);

                log.ErrorEvent("1");
                XAssert.IsTrue(listener.HasFailures);
                XAssert.IsTrue(listener.HasFailuresOrWarnings);
            }

            using (var listener = new TrackingEventListener(Events.Log))
            {
                Events log = Events.Log;

                log.CriticalEvent("1");

                XAssert.IsTrue(listener.HasFailures);
                XAssert.IsTrue(listener.HasFailuresOrWarnings);
            }

            using (var listener = new TrackingEventListener(Events.Log, warningMapper: _ => WarningState.AsError))
            {
                Events log = Events.Log;

                log.WarningEvent("1");

                XAssert.AreEqual(1, listener.TotalErrorCount);
                XAssert.IsTrue(listener.HasFailures);
                XAssert.IsTrue(listener.HasFailuresOrWarnings);
            }
        }

        [Fact]
        public void TrackingEventListenerHasFailuresOrWarnings()
        {
            using (var listener = new TrackingEventListener(Events.Log))
            {
                Events.Log.VerboseEvent("1");
                Events.Log.InfoEvent("1");
                Events.Log.AlwaysEvent("1");
                XAssert.IsFalse(listener.HasFailuresOrWarnings);
            }

            using (var listener = new TrackingEventListener(Events.Log))
            {
                Events.Log.WarningEvent("1");
                XAssert.IsTrue(listener.HasFailuresOrWarnings);
            }

            using (var listener = new TrackingEventListener(Events.Log))
            {
                Events.Log.ErrorEvent("1");
                XAssert.IsTrue(listener.HasFailuresOrWarnings);
            }

            using (var listener = new TrackingEventListener(Events.Log))
            {
                Events.Log.CriticalEvent("1");
                XAssert.IsTrue(listener.HasFailuresOrWarnings);
            }
        }

        [Fact]
        public void TestErrorCategorization()
        {
            using (var listener = new TrackingEventListener(Events.Log))
            {
                string testMessage = "Message from test event";
                Events.Log.UserErrorEvent(testMessage);
                XAssert.IsTrue(listener.HasFailures);
                XAssert.AreEqual(1, listener.UserErrorDetails.Count);
                XAssert.AreEqual(0, listener.InfrastructureErrorDetails.Count);
                XAssert.AreEqual(0, listener.InternalErrorDetails.Count);
                XAssert.AreEqual("UserErrorEvent", listener.UserErrorDetails.FirstErrorName);
                XAssert.IsTrue(listener.UserErrorDetails.FirstErrorMessage.Contains(testMessage));
            }

            using (var listener = new TrackingEventListener(Events.Log))
            {
                Events.Log.InfrastructureErrorEvent("1");
                XAssert.IsTrue(listener.HasFailures);
                XAssert.AreEqual(0, listener.UserErrorDetails.Count);
                XAssert.AreEqual(1, listener.InfrastructureErrorDetails.Count);
                XAssert.AreEqual(0, listener.InternalErrorDetails.Count);
                XAssert.AreEqual("InfrastructureErrorEvent", listener.InfrastructureErrorDetails.FirstErrorName);
            }

            using (var listener = new TrackingEventListener(Events.Log))
            {
                Events.Log.ErrorEvent("1");
                XAssert.IsTrue(listener.HasFailures);
                XAssert.AreEqual(0, listener.UserErrorDetails.Count);
                XAssert.AreEqual(0, listener.InfrastructureErrorDetails.Count);
                XAssert.AreEqual(1, listener.InternalErrorDetails.Count);
                XAssert.AreEqual("ErrorEvent", listener.InternalErrorDetails.FirstErrorName);
            }
        }

        [EventSource(Name = "TestSource")]
        private sealed class TestEventSource : EventSource
        {
            [Event(1, Level = EventLevel.Informational)]
            public void InfoEvent(string message)
            {
                WriteEvent(1, message);
            }
        }

        [Fact]
        public void TestMessageLabels()
        {
            string text;
            string alwaysEventLabel = "message";
            string criticalEventLabel = "critical";
            string errorEventLabel = "error";
            string warningEventLabel = "warning";
            string suppressedWarningEventLabel = "NoWarn";
            string infoEventLabel = "info";
            string verboseEventLabel = "verbose";

            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                using (var listener = new TextWriterEventListener(Events.Log, writer, DateTime.UtcNow, warningNumber => WarningState.AsWarning))
                {
                    Events log = Events.Log;

                    log.AlwaysEvent("Cookie 1");
                    log.CriticalEvent("Cookie 2");
                    log.ErrorEvent("Cookie 3");
                    log.WarningEvent("Cookie 4");
                    log.InfoEvent("Cookie 5");
                    log.VerboseEvent("Cookie 6");
                }

                text = writer.ToString();
            }

            XAssert.IsTrue(Regex.IsMatch(text, $"^{alwaysEventLabel} DX{(int)EventId.AlwaysEvent:D4}: Cookie 1\\r?$", RegexOptions.Multiline));
            XAssert.IsTrue(Regex.IsMatch(text, $"^{criticalEventLabel} DX{(int)EventId.CriticalEvent:D4}: Cookie 2\\r?$", RegexOptions.Multiline));
            XAssert.IsTrue(Regex.IsMatch(text, $"^{errorEventLabel} DX{(int)EventId.ErrorEvent:D4}: Cookie 3\\r?$", RegexOptions.Multiline));
            XAssert.IsTrue(Regex.IsMatch(text, $"^{warningEventLabel} DX{(int)EventId.WarningEvent:D4}: Cookie 4\\r?$", RegexOptions.Multiline));
            XAssert.IsTrue(Regex.IsMatch(text, $"^{infoEventLabel} DX{(int)EventId.InfoEvent:D4}: Cookie 5\\r?$", RegexOptions.Multiline));
            XAssert.IsTrue(Regex.IsMatch(text, $"^{verboseEventLabel} DX{(int)EventId.VerboseEvent:D4}: Cookie 6\\r?$", RegexOptions.Multiline));

            // suppress the warning message (similar to passing /noWarn:10002 (EventId.WarningEvent))
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                using (var listener = new TextWriterEventListener(Events.Log, writer, DateTime.UtcNow, warningNumber => WarningState.Suppressed))
                {
                    Events log = Events.Log;

                    // although we are suppressing the warning, it still should be captured (suppression only applies to console output and err/wrn files)
                    // however, this time a different label should be used
                    log.WarningEvent("Cookie 4");
                }

                text = writer.ToString();
            }

            XAssert.IsTrue(Regex.IsMatch(text, $"^{suppressedWarningEventLabel} DX{(int)EventId.WarningEvent:D4}: Cookie 4\\r?$", RegexOptions.Multiline));
        }
    }
}
