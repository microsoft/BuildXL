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
    }
}
