// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Tracing
{
    /// <summary>
    /// Helper to track unexpected conditions
    /// </summary>
    public static class UnexpectedCondition
    {
        /// <summary>
        /// Maximum number of unexpected conditions to send to telemetry
        /// </summary>
        public const int MaxTelemetryUnexpectedConditions = 5;
        
        private static int s_unexpectedConditionsLogged = 0;
        private static string s_currentSessionId = string.Empty;
        private static readonly object s_unexpectedConditionLoggerLock = new object();
    
        /// <summary>
        /// Logs an UnexpectedCondition both locally as a warning and a capped number of times per build session to telemetry.
        /// Always call this wrapper function
        /// </summary>
        public static void Log(LoggingContext loggingContext, string description)
        {
            lock (s_unexpectedConditionLoggerLock)
            {
                // Check and reset the context for what build session this in case the server process is reused
                if (s_currentSessionId != loggingContext.GetRootContext().Session.Id)
                {
                    s_currentSessionId = loggingContext.GetRootContext().Session.Id;
                    s_unexpectedConditionsLogged = 0;
                }

                // Guard against overflowing in case a huge number of unexpected conditions happen
                if (s_unexpectedConditionsLogged < int.MaxValue)
                {
                    s_unexpectedConditionsLogged++;
                }

                // Only log to telemetry fir the first 5 unexpected conditions per build session
                if (s_unexpectedConditionsLogged <= MaxTelemetryUnexpectedConditions)
                {
                    Logger.Log.UnexpectedConditionTelemetry(loggingContext, description);
                }
                else
                {
                    Logger.Log.UnexpectedConditionLocal(loggingContext, description);
                }
            }
        }
    }
}
