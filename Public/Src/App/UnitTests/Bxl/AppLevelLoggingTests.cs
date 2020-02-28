// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL
{
    public class AppLevelLoggingTests
    {
        [Fact]
        public void UnexpectedConditionTelemetryCapTest()
        {
            using (var listener = new TrackingEventListener(Events.Log))
            {
                listener.RegisterEventSource(global::BuildXL.Tracing.ETWLogger.Log);
                LoggingContext context = new LoggingContext("Test");
                int maxTelemetryUnexpectedConditions = global::BuildXL.Tracing.Logger.MaxTelemetryUnexpectedConditions;

                for (int i = 0; i < maxTelemetryUnexpectedConditions + 1; i++)
                {
                    global::BuildXL.Tracing.Logger.Log.UnexpectedCondition(context, "UnexpectedConditionTest");
                }

                // Make sure only 5 of the 6 events went to telemetry. The 6th events should only go local
                XAssert.AreEqual(maxTelemetryUnexpectedConditions, listener.CountsPerEventId((int)EventId.UnexpectedConditionTelemetry));
                XAssert.AreEqual(1, listener.CountsPerEventId((int)EventId.UnexpectedConditionLocal));

                // Now change the logging context for a new session and make sure the event goes to telemetry again
                context = new LoggingContext("Test2");
                global::BuildXL.Tracing.Logger.Log.UnexpectedCondition(context, "UnexpectedConditionTest");
                XAssert.AreEqual(maxTelemetryUnexpectedConditions + 1, listener.CountsPerEventId((int)EventId.UnexpectedConditionTelemetry));
                XAssert.AreEqual(1, listener.CountsPerEventId((int)EventId.UnexpectedConditionLocal));
            }
        }
    }
}
