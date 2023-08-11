// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using BuildXL;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using TestEvents = Test.BuildXL.Utilities.TestEvents;

namespace Test.BuildXL
{
    public class ConsoleEventListenerTests
    {
        [Fact]
        public void ConsoleEventListenerBasicTest()
        {
            TestEvents log = TestEvents.Log;

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
                    using (var listener = new ConsoleEventListener(Events.Log, console, baseTime, false, CancellationToken.None))
                    {
                        listener.RegisterEventSource(TestEvents.Log);

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
        public void CustomPipDescription()
        {
            string pipHash = "PipB9ACFCBECDA09F1F";
            string somePipInformation = "xunit.console.exe, Test.Sdk, StandardSdk.Testing.testingTest, debug-net451";
            string pipCustomDescription = "some custom description";

            string eventMessage = $"[{pipHash}, {somePipInformation}{FormattingEventListener.CustomPipDescriptionMarker}{pipCustomDescription}]";
            string eventMessageWithoutCustomDescription = $"[{pipHash}, {somePipInformation}]";
            string expectedShortenedMessage = $"[{pipHash}, {pipCustomDescription}]";

            using (var console = new MockConsole())
            {
                using (var listener = new ConsoleEventListener(Events.Log, console, DateTime.UtcNow, true, CancellationToken.None))
                {
                    listener.RegisterEventSource(TestEvents.Log);

                    // check that the message is shortened
                    TestEvents.Log.ErrorEvent(eventMessage);
                    console.ValidateCall(MessageLevel.Error, expectedShortenedMessage);
                    TestEvents.Log.WarningEvent(eventMessage);
                    console.ValidateCall(MessageLevel.Warning, expectedShortenedMessage);
                    // event message should remain unchanged since there is no custom description in it
                    TestEvents.Log.ErrorEvent(eventMessageWithoutCustomDescription);
                    console.ValidateCall(MessageLevel.Error, eventMessageWithoutCustomDescription);
                }
            }

            // now all messages should be 'displayed' as-is
            using (var console = new MockConsole())
            {
                using (var listener = new ConsoleEventListener(Events.Log, console, DateTime.UtcNow, false, CancellationToken.None))
                {
                    listener.RegisterEventSource(TestEvents.Log);

                    TestEvents.Log.ErrorEvent(eventMessage);
                    console.ValidateCall(MessageLevel.Error, eventMessage);
                    TestEvents.Log.WarningEvent(eventMessage);
                    console.ValidateCall(MessageLevel.Warning, eventMessage);
                    TestEvents.Log.ErrorEvent(eventMessageWithoutCustomDescription);
                    console.ValidateCall(MessageLevel.Error, eventMessageWithoutCustomDescription);
                }
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void NoWarningsToConsoleForActiveCancellationToken(bool suppressWarning)
        {
            string warningMessage = "I'm a warning you want to ignore; it hurts.";
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;

            // suppressWarning, else log the warnings
            if (suppressWarning)
            {
                // cancel the token source
                cancellationTokenSource.Cancel();
            }

            using (var console = new MockConsole())
            using (var listener = new ConsoleEventListener(Events.Log, console, DateTime.UtcNow,false, cancellationToken))
            {
                logWarning(listener);
                if (suppressWarning)
                {
                    console.ValidateNoCall();
                } 
                else
                {
                    console.ValidateCall(MessageLevel.Warning, warningMessage);
                }
            }

            void logWarning(ConsoleEventListener listener)
            {
                listener.RegisterEventSource(TestEvents.Log);
                TestEvents.Log.WarningEvent(warningMessage);                
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void NoErrorLoggingForActiveCancellationToken(bool doNotSuppressErrorLogging)
        {
            string errorMessage = "I'm an error you want to ignore; it hurts.";
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;

            // suppressError, else log the error
            if (doNotSuppressErrorLogging)
            {
                // cancel the token source
                cancellationTokenSource.Cancel();
            }

            using (var console = new MockConsole())
            using (var listener = new ConsoleEventListener(Events.Log, console, DateTime.UtcNow, false, cancellationToken))
            {
                logError(listener);
                if (doNotSuppressErrorLogging)
                {
                    console.ValidateNoCall();
                }
                else
                {
                    console.ValidateCall(MessageLevel.Error, errorMessage);
                }
            }

            void logError(ConsoleEventListener listener)
            {
                listener.RegisterEventSource(TestEvents.Log);
                TestEvents.Log.ErrorEvent(errorMessage);
            }
        }


        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void NoWarningsToConsole(bool isFromWorker)
        {
            string warningMessage = "I'm a warning you want to ignore; it hurts.";
            string warningName = "IgnoreWarning";
            var warningManager = new WarningManager();
            warningManager.SetState((int)TestEvents.EventId.WarningEvent, WarningState.Suppressed);

            // suppress the warning and check that it is not printed
            using (var console = new MockConsole())
            using (var listener = new ConsoleEventListener(Events.Log, console, DateTime.UtcNow, false, CancellationToken.None, warningMapper: warningManager.GetState))
            {
                logWarning(console, listener);
                console.ValidateNoCall();
            }

            // allow the warning
            using (var console = new MockConsole())
            using (var listener = new ConsoleEventListener(Events.Log, console, DateTime.UtcNow, false, CancellationToken.None))
            {
                logWarning(console, listener);
                console.ValidateCall(MessageLevel.Warning, warningMessage);
            }

            void logWarning(MockConsole console, ConsoleEventListener listener)
            {
                listener.RegisterEventSource(TestEvents.Log);
                listener.RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
                if (isFromWorker)
                {
                    global::BuildXL.Engine.Tracing.Logger.Log.DistributionWorkerForwardedWarning(BuildXLTestBase.CreateLoggingContextForTest(), new WorkerForwardedEvent()
                    {
                        EventId = (int)TestEvents.EventId.WarningEvent,
                        EventName = warningName,
                        EventKeywords = 0,
                        Text = warningMessage,
                    });
                }
                else
                {
                    TestEvents.Log.WarningEvent(warningMessage);
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

        [Fact]
        public void TestEventRedirection()
        {
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;
            var loggingContext = BuildXLTestBase.CreateLoggingContextForTest();

            using var console = new MockConsole();
            // Let's create and register a redirector listener to send an arbitrary message to the console
            using var redirectorListener = new ConsoleRedirectorEventListener(Events.Log, DateTime.UtcNow, new List<int> { (int)TestEvents.EventId.VerboseEvent }, loggingContext, warningMapper: null);
            using var consoleListener = new ConsoleEventListener(Events.Log, console, DateTime.UtcNow, false, cancellationToken);
            
            // Only register the events on the redirectorListener, so we don't get a false positive by the consoleListener reacting to the event:
            // this way, the message will get to the console by virtue of the redirector forwarding it to the console listener, and not by it picking up
            // the event from the ETW aether.
            redirectorListener.RegisterEventSource(TestEvents.Log);

            var message = "I'm an event that should be redirected to the console";
            TestEvents.Log.VerboseEvent(message);

            console.ValidateCall(MessageLevel.Info, message);
        }

        [Fact]
        public void TestEventRedirectionOfForwardedEvents()
        {
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;
            var loggingContext = BuildXLTestBase.CreateLoggingContextForTest();

            using var console = new MockConsole();
            
            // Let's create and register a redirector listener to send an arbitrary message to the console
            using var redirectorListener = new ConsoleRedirectorEventListener(Events.Log, DateTime.UtcNow, new List<int> { (int)TestEvents.EventId.VerboseEvent }, loggingContext, warningMapper: null);
            using var listener = new ConsoleEventListener(Events.Log, console, DateTime.UtcNow, false, cancellationToken);
            
            // Register the "payload" event on the redirector listener and log it as a forwarded event
            // The redirector listener will unpack the inner message and replay it in the console
            redirectorListener.RegisterEventSource(TestEvents.Log);
            var message = "I'm an event that should be redirected to the console";
            global::BuildXL.Engine.Tracing.Logger.Log.DistributionWorkerForwardedEvent(loggingContext, new WorkerForwardedEvent()
            {
                Text = message,
                EventId = (int)TestEvents.EventId.VerboseEvent
            });

            TestEvents.Log.VerboseEvent(message);
            console.ValidateCall(MessageLevel.Info, message);
        }
    }
}
