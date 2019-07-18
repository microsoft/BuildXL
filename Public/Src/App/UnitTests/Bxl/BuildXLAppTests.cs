// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL;
using BuildXL.App.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using System.Collections.Generic;
using System.Xml;
using System.Reflection;
using System.Diagnostics.Tracing;
using System;

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
                var userErrorClassification = BuildXLApp.ClassifyFailureFromLoggedEvents(listener);
                XAssert.AreEqual(ExitKind.UserError, userErrorClassification.ExitKind);
                XAssert.AreEqual("UserErrorEvent", userErrorClassification.ErrorBucket);

                // Now add an infrasctructure error. This should take prescedence
                Events.Log.InfrastructureErrorEvent("1");
                var infrastructureErrorClassification = BuildXLApp.ClassifyFailureFromLoggedEvents(listener);
                XAssert.AreEqual(ExitKind.InfrastructureError, infrastructureErrorClassification.ExitKind);
                XAssert.AreEqual("InfrastructureErrorEvent", infrastructureErrorClassification.ErrorBucket);

                // Finally add an internal error. Again, this takes highest prescedence
                Events.Log.ErrorEvent("1");
                var internalErrorClassification = BuildXLApp.ClassifyFailureFromLoggedEvents(listener);
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
                global::BuildXL.Engine.Tracing.Logger.Log.DistributionExecutePipFailedNetworkFailure(loggingContext, "ArbitraryPip", "ArbitraryWorker", "ArbitraryMessage", "ArbitraryStep");
                global::BuildXL.Scheduler.Tracing.Logger.Log.PipMaterializeDependenciesFromCacheFailure(loggingContext, "ArbitraryPip", "ArbitraryMessage");

                var infrastructureErrorClassification = BuildXLApp.ClassifyFailureFromLoggedEvents(listener);
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
        /// Having more than one event with the same EventId will cause chaos. Make sure it doesn't happen
        /// </summary>
        /// <remarks>
        /// This test leverage's EventSource's support for generating a manifes file to get all of the possible events
        /// that could be logged. This is maybe a bit roundabout but it the legacy of a time when an ETW manifest file
        /// was generated as part of the core product
        /// </remarks>
        [Fact]
        public void EventIdsDontOverlap()
        {
            TestEventListener testEventListener = new TestEventListener(Events.Log, "ThisTest");
            Events.Log.VerboseEvent("asdf");
            System.Diagnostics.Debugger.Launch();

            // Track the guids of all providers to create combined list
            List<Guid> providers = new List<Guid>();

            // First we load the original event source. We will then merge all other manfests into this file
            XmlDocument outerDoc = new XmlDocument();
            var type = Events.Log.GetType();
            var location = Assembly.GetAssembly(Events.Log.GetType()).Location;
            outerDoc.LoadXml(EventSource.GenerateManifest(Events.Log.GetType(), Assembly.GetAssembly(Events.Log.GetType()).Location));
            XmlNamespaceManager manager = new XmlNamespaceManager(outerDoc.NameTable);
            manager.AddNamespace("x", outerDoc.DocumentElement.NamespaceURI);

            providers.Add(Events.Log.Guid);

            // Nodes used as markers to merge other nodes
            XmlNode outerProvider = outerDoc.SelectSingleNode("//x:provider", manager);
            XmlNode outerStringTable = outerDoc.SelectSingleNode("//x:stringTable", manager);

            // Each additional manifest gets merged into the primary one
            foreach (var eventSource in global::BuildXL.BuildXLApp.GeneratedEventSources)
            {
                providers.Add(eventSource.Guid);
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(EventSource.GenerateManifest(eventSource.GetType(), Assembly.GetAssembly(eventSource.GetType()).Location));

                XmlNode provider = doc.SelectSingleNode("//x:provider", manager);
                XmlNode stringTable = doc.SelectSingleNode("//x:stringTable", manager);

                if (provider != null)
                {
                    outerProvider.ParentNode.AppendChild(outerDoc.ImportNode(provider, true));
                }

                if (stringTable != null)
                {
                    foreach (XmlNode entry in stringTable.ChildNodes)
                    {
                        outerStringTable.AppendChild(outerDoc.ImportNode(entry, true));
                    }
                }
            }

            using (MemoryStream ms = new MemoryStream())
            {
                outerDoc.Save(ms);
                ms.Position = 0;
                StreamReader reader = new StreamReader(ms);
                string manifestData = reader.ReadToEnd();
                XmlDocument newDoc = new XmlDocument();
                newDoc.LoadXml(manifestData.Replace(" xmlns=", " nope="));

                Dictionary<string, string> events = new Dictionary<string, string>();

                var nodeSet = newDoc.SelectNodes(@"//event");
                if (nodeSet == null || nodeSet.Count == 1)
                {
                    XAssert.Fail("Couldn't find any events. Test must be broken");
                }

                foreach (XmlNode node in nodeSet)
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
                    if (events.TryGetValue(eventId, out oldEventName))
                    {
                        XAssert.Fail("error: encountered duplicate event id:{0}. Existing Event:{1}, New Event:{2}", eventId, oldEventName, eventName);
                    }
                    else
                    {
                        events.Add(eventId, eventName);
                    }
                }
            }
        }
    }
}
