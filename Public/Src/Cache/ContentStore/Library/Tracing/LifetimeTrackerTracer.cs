// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    /// "Tracks" the key startup metrics.
    /// </summary>
    /// <remarks>
    /// <see cref="LifetimeTrackerTracer"/> remarks section for more details.
    /// </remarks>
    internal class LifetimeTracker
    {
        private readonly IClock _clock;
        private readonly DateTime _processStartTimeUtc;

        private readonly Result<DateTime> _shutdownTimeUtc;
        private readonly DateTime _serviceStartedTimeUtc;

        private Result<DateTime> _serviceReadyTimeUtc = new Result<DateTime>("Not available");

        private LifetimeTracker(
            IClock clock,
            TimeSpan startupDuration,
            DateTime processStartupTimeUtc,
            Result<DateTime> shutdownTimeUtc)
        {
            _clock = clock;
            StartupDuration = startupDuration;
            _serviceStartedTimeUtc = _clock.UtcNow;
            _shutdownTimeUtc = shutdownTimeUtc;
            _processStartTimeUtc = processStartupTimeUtc;
        }

        /// <nodoc />
        public static LifetimeTracker Started(IClock? clock, TimeSpan startupDuration, Result<DateTime> shutdownTimeUtc, DateTime processStartupTimeUtc)
            => new LifetimeTracker(clock ?? SystemClock.Instance, startupDuration, processStartupTimeUtc, shutdownTimeUtc);

        public TimeSpan StartupDuration { get; }

        public TimeSpan FullStartupDuration => _serviceStartedTimeUtc - _processStartTimeUtc;

        public Result<TimeSpan> RestartDelay => _shutdownTimeUtc.Then(st => Result.Success(_processStartTimeUtc - st));

        public Result<TimeSpan> FullInitializationDuration => _serviceReadyTimeUtc.Then(srt => Result.Success(srt - _processStartTimeUtc));

        public Result<TimeSpan> OfflineTime
        {
            get
            {
                if (_serviceReadyTimeUtc.Succeeded && _shutdownTimeUtc.Succeeded)
                {
                    return _serviceReadyTimeUtc.Value - _shutdownTimeUtc.Value;
                }
                else
                {
                    return new Result<TimeSpan>("Unavailable");
                }
            }
        }
        /// <nodoc />
        public void Ready()
        {
            _serviceReadyTimeUtc = _clock.UtcNow;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"StartupDuration=[{StartupDuration}], FullStartupDuration=[{FullStartupDuration}], RestartDelay=[{RestartDelay.ToStringWithValue()}], FullInitializationDuration=[{FullInitializationDuration.ToStringWithValue()}], OfflineTime=[{OfflineTime.ToStringWithValue()}]";
        }
    }

    /// <summary>
    /// A set of tracing methods responsible for tracking the service's lifetime.
    /// </summary>
    /// <remarks>
    /// This tracer is responsible for tracking the following important information about the service's lifetime:
    /// 1. Service startup duration: ServiceStarted - ServiceStarting
    /// 2. Full startup duration: ServiceStarted - ProcessStartTime
    /// 3. Restart delay: ProcessStartTime - LastShutdownTime
    /// 
    /// 4. Full initialization duration: ServiceReadyTime - ProcessStartTime
    /// 5. Offline time: ServiceReadyTime - LastShutdownTime
    /// </remarks>
    public class LifetimeTrackerTracer
    {
        private const string ComponentName = "LifetimeTracker";

        /// <summary>
        /// Tracks the service's lifetime.
        /// </summary>
        private static Result<LifetimeTracker> LifetimeTracker = new Result<LifetimeTracker>("Not available");

        /// <summary>
        /// Trace that the service is starting.
        /// </summary>
        public static void StartingService(Context context)
        {
            Trace(context, "Starting CaSaaS instance");
        }

        /// <summary>
        /// Trace that the service started successfully but it doesn't mean that its ready to process incoming requests.
        /// The service is fully ready only when <see cref="ServiceReadyToProcessRequests"/> is called.
        /// </summary>
        public static void ServiceStarted(Context context, TimeSpan startupDuration, Result<DateTime> shutdownTimeUtc, IClock? clock = null)
        {
            LifetimeTracker = Tracing.LifetimeTracker.Started(clock, startupDuration, shutdownTimeUtc, GetProcessStartupTimeUtc());
            Trace(context, "CaSaaS started.");
        }

        private static DateTime GetProcessStartupTimeUtc() => Process.GetCurrentProcess().StartTime.ToUniversalTime();

        /// <summary>
        /// Trace that service initialization has failed.
        /// </summary>
        public static void ServiceStartupFailed(Context context, Exception e, TimeSpan startupDuration)
        {
            Trace(context, $"CaSaaS initialization failed by {startupDuration.TotalMilliseconds}ms with error: {e}");
        }

        /// <summary>
        /// Trace that the service is fully initialized and ready to process incoming requests.
        /// </summary>
        public static void ServiceReadyToProcessRequests(Context context)
        {
            var lifetime = LifetimeTracker;
            if (lifetime.Succeeded)
            {
                lifetime.Value!.Ready();
            }

            Trace(context, $"CaSaaS instance is fully initialized and ready to process requests. {lifetime.ToStringWithValue()}");
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

        /// <summary>
        /// Trace that a graceful shutdown was initiated.
        /// </summary>
        public static void TeardownRequested(Context context, string reason)
        {
            Trace(context, $"Teardown is requested. Reason={reason}.");
        }

        private static void Trace(Context context, string message, Severity severity = Severity.Info, [CallerMemberName]string? operation = null)
        {
            context.TraceMessage(severity, message, ComponentName, operation);
        }
    }
}
