// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Reflection;
using System.Text;
using System.Xml;
using BuildXL;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL
{
    public class BuildXLAppTests
    {
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
                global::BuildXL.Engine.Tracing.Logger.Log.DistributionWorkerForwardedError(loggingContext, new WorkerForwardedEvent()
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
            XAssert.AreEqual($"{Branding.ProductExecutableName} /first [...] /tenth", BuildXLApp.ScrubCommandLine($"{Branding.ProductExecutableName} /first /second /third /fourth /fifth /sixth /seventh /eight /ninth /tenth", 20, 10));
            XAssert.AreEqual($"{Branding.ProductExecutableName} /first [...]nth", BuildXLApp.ScrubCommandLine($"{Branding.ProductExecutableName} /first /second /tenth", 20, 3));
            XAssert.AreEqual("bxl.[...]nth", BuildXLApp.ScrubCommandLine($"{Branding.ProductExecutableName} /first /second /tenth", 4, 3));
            XAssert.AreEqual($"{Branding.ProductExecutableName} /first /second /tenth", BuildXLApp.ScrubCommandLine($"{Branding.ProductExecutableName} /first /second /tenth", 4, 332));
            XAssert.AreEqual($"{Branding.ProductExecutableName} /first /second /tenth", BuildXLApp.ScrubCommandLine($"{Branding.ProductExecutableName} /first /second /tenth", 432, 2));
            XAssert.AreEqual("[...]", BuildXLApp.ScrubCommandLine($"{Branding.ProductExecutableName} /first /second /tenth", 0, 0));
            XAssert.AreEqual("", BuildXLApp.ScrubCommandLine("", 1, 1));
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

        [Theory]
        [InlineData(Infra.CloudBuild, DistributedBuildRoles.Master, ExecutionEnvironment.OfficeMetaBuildDev, "0100-ffff-0f2d")]
        [InlineData(Infra.Ado, DistributedBuildRoles.Orchestrator, ExecutionEnvironment.OfficeMetaBuildDev, "0300-eeee-0f2d")]
        [InlineData(Infra.Developer, DistributedBuildRoles.None, ExecutionEnvironment.OfficeMetaBuildDev, "0000-0000-0f2d")]
        [InlineData(Infra.Developer, DistributedBuildRoles.None, ExecutionEnvironment.OfficeProductBuildLab, "0000-0000-0f37")]
        [InlineData(Infra.Developer, DistributedBuildRoles.None, ExecutionEnvironment.SelfHostLKG, "0000-0000-bd07")]
        public void TestSessionIdGeneration(Infra infra, DistributedBuildRoles buildRole, ExecutionEnvironment executionEnvironment, string expectedSubString)
        {
            var config = new ConfigurationImpl();
            config.Infra = infra;
            config.Logging.Environment = executionEnvironment;
            config.Distribution.BuildRole = buildRole;

            var guid = System.Guid.NewGuid();
            var stringGuid = guid.ToString();
            var firstDash = stringGuid.IndexOf('-');
            var expectedString = $"-{expectedSubString}{stringGuid[stringGuid.LastIndexOf('-')..]}";

            var sessionId = BuildXLApp.ComputeSessionId(guid, config).ToString();

            XAssert.AreNotEqual(stringGuid[..firstDash], sessionId[..firstDash], "The first blocks should not match");
            XAssert.AreEqual(expectedString, sessionId[firstDash..]);
        }
    }
}
