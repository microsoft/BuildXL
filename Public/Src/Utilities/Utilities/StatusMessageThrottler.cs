// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Determines whether a status message should be throttled. When honored by the caller, creates a steadily
    /// decreasing update period to limit the overall number of status messages logged during a build.
    /// </summary>
    public class StatusMessageThrottler
    {
        private readonly DateTime m_baseTimeUTC;
        private DateTime m_lastStatusLogTimeUTC;

        /// <nodoc/>
        public StatusMessageThrottler(DateTime baseTimeUTC) 
        {
            m_baseTimeUTC = baseTimeUTC;
        }

        /// <summary>
        /// Returns true if the status update should be throttled
        /// </summary>
        public bool ShouldThrottleStatusUpdate()
        {
            var result = ShouldThrottleStatusUpdate(m_lastStatusLogTimeUTC, m_baseTimeUTC, DateTime.UtcNow, out DateTime newLastStatusLogTime);
            m_lastStatusLogTimeUTC = newLastStatusLogTime;
            return result;
        }

        /// <summary>
        /// Internal method exposing parameters intended for unit testing
        /// </summary>
        internal static bool ShouldThrottleStatusUpdate(DateTime lastStatusLogTime, DateTime baseTime, DateTime currentTime, out DateTime newLastStatusLogTime)
        {
            newLastStatusLogTime = lastStatusLogTime;
            int targetUpdatePeriodMs;
            switch ((currentTime - baseTime).TotalMinutes)
            {
                case <= 1:
                    // No throttling under a minute
                    targetUpdatePeriodMs = int.MinValue;
                    break;
                case < 5:
                    targetUpdatePeriodMs = 10_000;
                    break;
                case < 30:
                    targetUpdatePeriodMs = 20_000;
                    break;
                case < 60:
                    targetUpdatePeriodMs = 30_000;
                    break;
                default:
                    targetUpdatePeriodMs = 60_000;
                    break;
            }

            if ((currentTime - lastStatusLogTime).TotalMilliseconds > targetUpdatePeriodMs)
            {
                newLastStatusLogTime = currentTime;
                return false;
            }

            return true;
        }
    }
}
