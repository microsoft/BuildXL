// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    /// A set of tracing methods responsible for tracking the service's lifetime.
    /// </summary>
    public class LifetimeTrackerTracer
    {
        private const string ComponentName = "LifetimeTracker";

        /// <nodoc />
        public static void StartingService(Context context)
        {
            Trace(context, "Starting CaSaaS instance");
        }

        /// <nodoc />
        public static void ServiceStarted(Context context, Result<TimeSpan> offlineTimeResult, TimeSpan startupDuration)
        {
            var offlineTimeResultString = offlineTimeResult.Succeeded ? offlineTimeResult.Value.ToString() : offlineTimeResult.ToString();
            var timeFromProcessStart = GetTimeFromProcessStart();
            Trace(context, $"CaSaaS started. StartupDuration=[{startupDuration}], FullStartupDuration=[{timeFromProcessStart}], OfflineTime=[{offlineTimeResultString}]");
        }

        private static TimeSpan GetTimeFromProcessStart()
        {
            return DateTime.UtcNow - Process.GetCurrentProcess().StartTime;
        }

        /// <nodoc />
        public static void ServiceStartupFailed(Context context, Exception e, TimeSpan startupDuration)
        {
            Trace(context, $"CaSaaS initialization failed by {startupDuration.TotalMilliseconds}ms with error: {e}");
        }

        /// <nodoc />
        public static void ServiceReadyToProcessRequests(Context context)
        {
            var timeFromProcessStart = GetTimeFromProcessStart();
            Trace(context, $"CaSaaS instance is fully initialized and ready to process requests. FullInitializationDuration=[{timeFromProcessStart}]");
        }

        /// <nodoc />
        public static void ShuttingDownService(Context context)
        {
            Trace(context, "Shutting down CaSaaS instance");
        }

        /// <nodoc />
        public static void ServiceStopped(Context context, BoolResult result, TimeSpan shutdownDuration)
        {
            if (!result)
            {
                Trace(context, $"CaSaaS instance failed to stop by {shutdownDuration.TotalMilliseconds}ms: {result}", Severity.Warning);
            }
            else
            {
                Trace(context, $"CaSaaS instance stopped successfully by {shutdownDuration.TotalMilliseconds}ms.");
            }
        }

        /// <nodoc />
        public static void TeardownRequested(Context context, string reason)
        {
            Trace(context, $"Teardown is requested. Reason={reason}.");
        }

        private static void Trace(Context context, string message, Severity severity = Severity.Info, [CallerMemberName]string operation = null)
        {
            context.TraceMessage(severity, message, ComponentName, operation);
        }
    }
}
