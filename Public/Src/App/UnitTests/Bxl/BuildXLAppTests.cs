// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Reflection;
using System.Text;
using System.Xml;
using BuildXL;
using BuildXL.App.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL
{
    public class BuildXLAppTests
    {
        [Fact]
        public void ErrorPrecedence()
        {
            using (var listener = new TrackingEventListener(Events.Log))
            {
                Events.Log.UserErrorEvent("1");
                var userErrorClassification = BuildXLApp.ClassifyFailureFromLoggedEvents(Events.StaticContext, listener);
                XAssert.AreEqual(ExitKind.UserError, userErrorClassification.ExitKind);
                XAssert.AreEqual("UserErrorEvent", userErrorClassification.ErrorBucket);

                // Now add an infrasctructure error. This should take prescedence
                Events.Log.InfrastructureErrorEvent("1");
                var infrastructureErrorClassification = BuildXLApp.ClassifyFailureFromLoggedEvents(Events.StaticContext, listener);
                XAssert.AreEqual(ExitKind.InfrastructureError, infrastructureErrorClassification.ExitKind);
                XAssert.AreEqual("InfrastructureErrorEvent", infrastructureErrorClassification.ErrorBucket);

                // Finally add an internal error. Again, this takes highest prescedence
                Events.Log.ErrorEvent("1");
                var internalErrorClassification = BuildXLApp.ClassifyFailureFromLoggedEvents(Events.StaticContext, listener);
                XAssert.AreEqual(ExitKind.InternalError, internalErrorClassification.ExitKind);
                XAssert.AreEqual("ErrorEvent", internalErrorClassification.ErrorBucket);
            }
        }

        [Fact]
        public void DistributedBuildConnectivityIssueTrumpsOtherErrors()
        {
            var loggingContext = XunitBuildXLTest.CreateLoggingContextForTest();

            using (var listener = new TrackingEventListener(Events.Log))
            {
                listener.RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
                listener.RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
                global::BuildXL.Scheduler.Tracing.Logger.Log.PipMaterializeDependenciesFromCacheFailure(loggingContext, "ArbitraryPip", "ArbitraryMessage");
                global::BuildXL.Engine.Tracing.Logger.Log.DistributionExecutePipFailedNetworkFailure(loggingContext, "ArbitraryPip", "ArbitraryWorker", "ArbitraryMessage", "ArbitraryStep", "ArbitraryCaller");
                global::BuildXL.Scheduler.Tracing.Logger.Log.PipMaterializeDependenciesFromCacheFailure(loggingContext, "ArbitraryPip", "ArbitraryMessage");

                var infrastructureErrorClassification = BuildXLApp.ClassifyFailureFromLoggedEvents(Events.StaticContext, listener);
                XAssert.AreEqual(ExitKind.InfrastructureError, infrastructureErrorClassification.ExitKind);
                XAssert.AreEqual(global::BuildXL.Engine.Tracing.LogEventId.DistributionExecutePipFailedNetworkFailure.ToString(), infrastructureErrorClassification.ErrorBucket);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestErrorReplayedFromWorker(bool isUserError)
        {
            using (var listener = new TrackingEventListener(Events.Log))
            {
                listener.RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
                const string ErrorName = "MyTestEvent";
                const string ErrorText = "Event logged from worker";
                LoggingContext loggingContext = BuildXL.TestUtilities.Xunit.XunitBuildXLTest.CreateLoggingContextForTest();
                global::BuildXL.Engine.Tracing.Logger.Log.DistributionWorkerForwardedError(loggingContext, new global::BuildXL.Engine.Tracing.WorkerForwardedEvent()
                {
                    EventId = 100,
                    EventName = ErrorName,
                    EventKeywords = isUserError ? (int)global::BuildXL.Utilities.Instrumentation.Common.Keywords.UserError : 0,
                    Text = ErrorText,
                });

                XAssert.IsTrue(listener.HasFailures);
                if (isUserError)
                {
                    XAssert.AreEqual(1, listener.UserErrorDetails.Count);
                    XAssert.AreEqual(0, listener.InternalErrorDetails.Count);
                    XAssert.AreEqual(ErrorName, listener.UserErrorDetails.FirstErrorName);
                    XAssert.AreEqual(ErrorText, listener.UserErrorDetails.FirstErrorMessage);
                }
                else
                {
                    XAssert.AreEqual(0, listener.UserErrorDetails.Count);
                    XAssert.AreEqual(1, listener.InternalErrorDetails.Count);
                    XAssert.AreEqual(ErrorName, listener.InternalErrorDetails.FirstErrorName);
                    XAssert.AreEqual(ErrorText, listener.InternalErrorDetails.FirstErrorMessage);
                }
            }
        }

        [Fact]
        public void TestScrubbingCommandLine()
        {
            XAssert.AreEqual($"{Branding.ProductExecutableName} /first [...] /tenth", Logger.ScrubCommandLine($"{Branding.ProductExecutableName} /first /second /third /fourth /fifth /sixth /seventh /eight /ninth /tenth", 20, 10));
            XAssert.AreEqual($"{Branding.ProductExecutableName} /first [...]nth", Logger.ScrubCommandLine($"{Branding.ProductExecutableName} /first /second /tenth", 20, 3));
            XAssert.AreEqual("bxl.[...]nth", Logger.ScrubCommandLine($"{Branding.ProductExecutableName} /first /second /tenth", 4, 3));
            XAssert.AreEqual($"{Branding.ProductExecutableName} /first /second /tenth", Logger.ScrubCommandLine($"{Branding.ProductExecutableName} /first /second /tenth", 4, 332));
            XAssert.AreEqual($"{Branding.ProductExecutableName} /first /second /tenth", Logger.ScrubCommandLine($"{Branding.ProductExecutableName} /first /second /tenth", 432, 2));
            XAssert.AreEqual("[...]", Logger.ScrubCommandLine($"{Branding.ProductExecutableName} /first /second /tenth", 0, 0));
            XAssert.AreEqual("", Logger.ScrubCommandLine("", 1, 1));
        }

        /// <summary>
        /// Having more than one event with the same EventId will cause chaos. Make sure it doesn't happen.
        /// </summary>
        [Fact]
        public void EventIdsDontOverlap()
        {
            Dictionary<string, string> allEncounteredEvents = new Dictionary<string, string>();
            StringBuilder errors = new StringBuilder();

            // Look at each generated event source to make sure it doesn't have any conflicts
            foreach (var eventSource in global::BuildXL.BuildXLApp.GeneratedEventSources)
            {
                int startCount = allEncounteredEvents.Count;
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(EventSource.GenerateManifest(eventSource.GetType(), Assembly.GetAssembly(eventSource.GetType()).Location));
                XmlNamespaceManager mgr = new XmlNamespaceManager(doc.NameTable);
                mgr.AddNamespace("x", doc.DocumentElement.NamespaceURI);

                foreach (XmlNode node in doc.SelectNodes(@"//x:event", mgr))
                {
                    string eventId = node.Attributes["value"].Value;
                    string eventName = node.Attributes["symbol"].Value;

                    // It seems every event provider gets a default event with id="0". Ignore this in the check since
                    // it will give false positives.
                    if (eventId == "0")
                    {
                        continue;
                    }

                    string oldEventName;
                    if (allEncounteredEvents.TryGetValue(eventId, out oldEventName))
                    {
                        errors.AppendLine($"error: encountered duplicate event id:{eventId}. Existing Event:{oldEventName}, New Event:{eventName}");
                    }
                    else
                    {
                        allEncounteredEvents.Add(eventId, eventName);
                    }
                }

                if (allEncounteredEvents.Count == startCount)
                {
                    errors.AppendLine($"Didn't found any events in EventSource: {eventSource.GetType().ToString()}");
                }
            }

            if (errors.Length != 0)
            {
                XAssert.Fail(errors.ToString());
            }
        }
    }
}
