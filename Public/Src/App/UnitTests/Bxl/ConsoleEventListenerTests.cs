// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL
{
    public class ConsoleEventListenerTests
    {
        private class MockConsole : IConsole
        {
            private MessageLevel m_lastMessageLevel;
            private string m_lastLine;

            public void Dispose()
            {
            }

            public void ReportProgress(ulong done, ulong total)
            {
            }

            public void WriteOutputLine(MessageLevel messageLevel, string line)
            {
                m_lastMessageLevel = messageLevel;
                m_lastLine = line;
            }

            public void ValidateCall(MessageLevel messageLevel, string lineEnd)
            {
                ValidateCall(messageLevel, null, lineEnd);
            }

            public void ValidateCall(MessageLevel messageLevel, string lineStart, string lineEnd)
            {
                XAssert.IsNotNull(m_lastLine, "WriteOutputLine was not called");
                XAssert.AreEqual(messageLevel, m_lastMessageLevel);

                if (lineStart != null)
                {
                    Assert.StartsWith(lineStart, m_lastLine);
                }

                if (lineEnd != null)
                {
                    Assert.EndsWith(lineEnd, m_lastLine);
                }

                m_lastLine = null;
            }

            /// <summary>
            /// Check that no messages were printed on the Console
            /// </summary>
            public void ValidateNoCall()
            {
                XAssert.IsNull(m_lastLine, "Console printed a message while it was not supposed to do it.");
            }

            public void WriteOverwritableOutputLine(MessageLevel messageLevel, string standardLine, string overwritableLine)
            {
                // noop
            }

            public void WriteOverwritableOutputLineOnlyIfSupported(MessageLevel messageLevel, string standardLine, string overwritableLine)
            {
                // noop
            }

            public bool AskUser()
            {
                return false;
            }
        }

        [Fact]
        public void ConsoleEventListenerBasicTest()
        {
            Events log = Events.Log;

            for (int i = 0; i < 4; i++)
            {
                DateTime baseTime = DateTime.UtcNow;
                if (i == 2)
                {
                    baseTime -= TimeSpan.FromHours(1);
                }
                else if (i == 3)
                {
                    baseTime -= TimeSpan.FromDays(1);
                }

                bool colorize = i == 0;

                using (var console = new MockConsole())
                {
                    using (var listener = new ConsoleEventListener(Events.Log, console, baseTime, false))
                    {
                        log.VerboseEvent("1");
                        console.ValidateCall(MessageLevel.Info, "1");
                        log.InfoEvent("2");
                        console.ValidateCall(MessageLevel.Info, "2");
                        log.WarningEvent("3");
                        console.ValidateCall(MessageLevel.Warning, "3");
                        log.ErrorEvent("4");
                        console.ValidateCall(MessageLevel.Error, "4");
                        log.CriticalEvent("5");
                        console.ValidateCall(MessageLevel.Error, "5");
                        log.AlwaysEvent("6");
                        console.ValidateCall(MessageLevel.Info, "6");
                        log.VerboseEventWithProvenance("Test.cs", 1, 2, "Bad juju");
                        console.ValidateCall(MessageLevel.Info, "Test.cs(1,2): ", "Bad juju");
                    }
                }
            }
        }

        [Fact]
        public void CustomPipDecsription()
        {
            string pipHash = "PipB9ACFCBECDA09F1F";
            string somePipInformation = "xunit.console.exe, Test.Sdk, StandardSdk.Testing.testingTest, debug-net451";
            string pipCustomDescription = "some custom description";

            string eventMessage = $"[{pipHash}, {somePipInformation}{FormattingEventListener.CustomPipDescriptionMarker}{pipCustomDescription}]";
            string eventMessageWithoutCustomDescription = $"[{pipHash}, {somePipInformation}]";
            string expectedShortenedMessage = $"[{pipHash}, {pipCustomDescription}]";

            using (var console = new MockConsole())
            {
                using (var listener = new ConsoleEventListener(Events.Log, console, DateTime.UtcNow, true))
                {
                    // check that the message is shortened
                    Events.Log.ErrorEvent(eventMessage);
                    console.ValidateCall(MessageLevel.Error, expectedShortenedMessage);
                    Events.Log.WarningEvent(eventMessage);
                    console.ValidateCall(MessageLevel.Warning, expectedShortenedMessage);
                    // event message should remain unchanged since there is no custom description in it
                    Events.Log.ErrorEvent(eventMessageWithoutCustomDescription);
                    console.ValidateCall(MessageLevel.Error, eventMessageWithoutCustomDescription);
                }
            }

            // now all messages should be 'displayed' as-is
            using (var console = new MockConsole())
            {
                using (var listener = new ConsoleEventListener(Events.Log, console, DateTime.UtcNow, false))
                {
                    Events.Log.ErrorEvent(eventMessage);
                    console.ValidateCall(MessageLevel.Error, eventMessage);
                    Events.Log.WarningEvent(eventMessage);
                    console.ValidateCall(MessageLevel.Warning, eventMessage);
                    Events.Log.ErrorEvent(eventMessageWithoutCustomDescription);
                    console.ValidateCall(MessageLevel.Error, eventMessageWithoutCustomDescription);
                }
            }
        }

        [Fact]
        public void NoWarningsToConsole()
        {
            string warningMessage = "I'm a warning you want to ignore; it hurts.";
            
            var warningManager = new WarningManager();
            warningManager.SetState((int)EventId.WarningEvent, WarningState.Suppressed);


            // suppress the warning and check that it is not printed
            using (var console = new MockConsole())
            {
                using (var listener = new ConsoleEventListener(Events.Log, console, DateTime.UtcNow, false, warningMapper: warningManager.GetState))
                {
                    Events.Log.WarningEvent(warningMessage);
                    console.ValidateNoCall();
                }
            }

            // allow the warning
            using (var console = new MockConsole())
            {
                using (var listener = new ConsoleEventListener(Events.Log, console, DateTime.UtcNow, false))
                {
                    Events.Log.WarningEvent(warningMessage);
                    console.ValidateCall(MessageLevel.Warning, warningMessage);
                }
            }
        }

        [Fact]
        public void TestPercentDone()
        {
            XAssert.AreEqual("100.00%  ", ConsoleEventListener.ComputePercentDone(23, 23, 0, 0));
            XAssert.AreEqual("100.00%  ", ConsoleEventListener.ComputePercentDone(23, 23, 43, 43));
            XAssert.AreEqual("99.99%  ", ConsoleEventListener.ComputePercentDone(99998, 99999, 43, 43));
            XAssert.AreEqual("99.99%  ", ConsoleEventListener.ComputePercentDone(23, 23, 99998, 99999));
            XAssert.AreEqual("99.95%  ", ConsoleEventListener.ComputePercentDone(1000, 1000, 50, 100));
            XAssert.AreEqual("99.95%  ", ConsoleEventListener.ComputePercentDone(9999, 10000, 50, 100));
        }
    }
}
