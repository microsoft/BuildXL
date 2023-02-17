// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    /// A utility class that helps computing service lifetime metrics.
    /// </summary>
    /// <remarks>
    /// <see cref="LifetimeTracker"/> remarks section for more details.
    /// </remarks>
    internal class LifetimeTrackerHelper
    {
        // The fields are declared from the oldest time (usually) to the newest.
        private readonly Result<DateTime> _lastKnownHeartbeatTimeUtc; // Last heartbeat of the previous process
        private readonly DateTime _processStartTimeUtc;
        private readonly DateTime _serviceStartingTimeUtc; // the time when the service began initialization

        private DateTime? _serviceStartedTimeUtc;
        private DateTime? _serviceReadyTimeUtc;

        private LifetimeTrackerHelper(
            DateTime serviceStartingTimeUtc,
            DateTime processStartTimeUtc,
            Result<DateTime> lastKnownHeartbeatTimeUtc)
        {
            _serviceStartingTimeUtc = serviceStartingTimeUtc;
            _lastKnownHeartbeatTimeUtc = lastKnownHeartbeatTimeUtc;
            _processStartTimeUtc = processStartTimeUtc;
        }

        /// <nodoc />
        public static LifetimeTrackerHelper Starting(DateTime nowUtc, DateTime processStartTimeUtc, Result<DateTime> lastKnownHeartbeatTimeUtc)
            => new LifetimeTrackerHelper(nowUtc, processStartTimeUtc, lastKnownHeartbeatTimeUtc);

        /// <summary>
        /// Notify that the service has started (but not necessarily ready to process the requests).
        /// </summary>
        public void Started(DateTime nowUtc) => _serviceStartedTimeUtc = nowUtc;

        /// <summary>
        /// Notifies that the service is ready processing requests (it still does not necessarily means that the service is fully ready
        /// because this method may be called before the startup is fully done).
        /// </summary>
        public void Ready(DateTime nowUtc) => _serviceReadyTimeUtc = nowUtc;

        /// <summary>
        /// The service is fully initialized when <see cref="Started"/> and <see cref="Ready(DateTime)"/> methods are called.
        /// </summary>
        public bool IsFullyInitialized() => _serviceStartedTimeUtc != null && _serviceReadyTimeUtc != null;

        /// <summary>
        /// The time took to initialize the service.
        /// </summary>
        public TimeSpan StartupDuration
        {
            get
            {
                Contract.Requires(IsFullyInitialized());
                Contract.Assert(_serviceStartedTimeUtc != null);

                return _serviceStartedTimeUtc.Value - _serviceStartingTimeUtc;
            }
        }

        /// <summary>
        /// The time took to initialized the service from the process's start time.
        /// </summary>
        public TimeSpan FullStartupDuration
        {
            get
            {
                Contract.Requires(IsFullyInitialized());
                Contract.Assert(_serviceStartedTimeUtc != null);
                return _serviceStartedTimeUtc.Value - _processStartTimeUtc;
            }
        }

        /// <summary>
        /// The time took to fully initialized the service (the time since the process start till the ready event).
        /// </summary>
        public TimeSpan FullInitializationDuration
        {
            get
            {
                Contract.Requires(IsFullyInitialized());
                return ServiceReadyTimeUtc - _processStartTimeUtc;
            }
        }

        /// <summary>
        /// The time when the service is ready to process the requests (the max of service started time and ready).
        /// </summary>
        public DateTime ServiceReadyTimeUtc
        {
            get
            {
                Contract.Requires(IsFullyInitialized());
                Contract.Assert(_serviceReadyTimeUtc != null);
                Contract.Assert(_serviceStartedTimeUtc != null);

                // Service ready time is the later of two: startup time and "service ready" time.
                return _serviceReadyTimeUtc.Value > _serviceStartedTimeUtc.Value ? _serviceReadyTimeUtc.Value : _serviceStartedTimeUtc.Value;
            }
        }

        /// <summary>
        /// The time between the last known heartbeat and the service is ready.
        /// </summary>
        public Result<TimeSpan> OfflineTime
        {
            get
            {
                Contract.Requires(IsFullyInitialized());
                return _lastKnownHeartbeatTimeUtc.Then(st => Result.Success(ServiceReadyTimeUtc - st));
            }
        }

        /// <summary>
        /// Returns the time between last known heartbeat of the previous service instance till the process startup time of the current instance.
        /// </summary>
        public Result<TimeSpan> RestartDelay => _lastKnownHeartbeatTimeUtc.Then(st => Result.Success(_processStartTimeUtc - st));

        /// <inheritdoc />
        public override string ToString()
        {
            return $"StartupDuration=[{StartupDuration}], FullStartupDuration=[{FullStartupDuration}], RestartDelay=[{RestartDelay.ToStringWithValue()}], FullInitializationDuration=[{FullInitializationDuration}], OfflineTime=[{OfflineTime.ToStringWithValue()}]";
        }
    }

    /// <summary>
    /// A set of tracing methods responsible for tracking the service's lifetime.
    /// </summary>
    /// <remarks>
    /// This tracer is responsible for tracking the following important information about the service's lifetime:
    /// 1. Service startup duration: ServiceStarted - ServiceStarting
    /// 2. Full startup duration: ServiceStarted - ProcessStartTime
    /// 3. Restart delay: ProcessStartTime - LastKnownHeartbeatTime
    /// 4. Service readiness: when the services is fully initialized (StartupAsync is done) *and* ready processing requests.
    /// 4. Full initialization duration: ServiceReadyTime - ProcessStartTime
    /// 5. Offline time: ServiceReadyTime - LastKnownHeartbeatTime
    /// </remarks>
    public class LifetimeTracker
    {
        /// <nodoc />
        public const string ComponentName = "LifetimeTracker";

        private static readonly Result<ServiceOfflineDurationTracker> ServiceRunningTrackerUnavailableResult = new Result<ServiceOfflineDurationTracker>($"{nameof(ServiceStarting)} method was not called yet.");
        private static readonly Result<LifetimeTrackerHelper> LifetimeTrackerHelperUnavailableResult = new Result<LifetimeTrackerHelper>($"{nameof(ServiceStarted)} method was not called yet.");

        private static Result<ServiceOfflineDurationTracker> ServiceRunningTrackerResult = ServiceRunningTrackerUnavailableResult;
        private static Result<LifetimeTrackerHelper> LifetimeTrackerHelper = LifetimeTrackerHelperUnavailableResult;

        /// <summary>
        /// Trace that the service is starting.
        /// </summary>
        public static void ServiceStarting(
            Context context,
            TimeSpan? serviceRunningLogInterval,
            AbsolutePath logFilePath,
            string? serviceName = null,
            IClock? clock = null,
            DateTime? processStartupTime = null)
        {
            var operationContext = new OperationContext(context);
            ServiceRunningTrackerResult = ServiceOfflineDurationTracker.Create(
                operationContext,
                SystemClock.Instance,
                new PassThroughFileSystem(),
                serviceRunningLogInterval,
                logFilePath,
                serviceName);

            var offlineTime = ServiceRunningTrackerResult.Then(r => r.GetLastServiceHeartbeatTime(operationContext));

            LifetimeTrackerHelper = Tracing.LifetimeTrackerHelper.Starting(clock.GetUtcNow(), processStartupTime ?? GetProcessStartupTimeUtc(), offlineTime.Then(v => Result.Success(v.lastServiceHeartbeatTime)));

            var runtime = OperatingSystemHelperExtension.GetRuntimeFrameworkNameAndVersion();
            Trace(context, $"Starting CaSaaS instance. Runtime={runtime}{offlineTime.ToStringSelect(r => $". LastHeartBeatTime={r.lastServiceHeartbeatTime}, ShutdownCorrectly={r.shutdownCorrectly}")}");
        }

        /// <summary>
        /// Trace that the service started successfully.
        /// </summary>
        public static void ServiceStarted(Context context, IClock? clock = null)
        {
            Trace(context, "CaSaaS started.");

            LifetimeTrackerHelper.Match(
                successAction: helper =>
                {
                    helper.Started(clock.GetUtcNow());
                    TraceIfReady(context, helper);
                },
                failureAction: r => TraceHelperIsNotAvailable(context, r));
        }

        /// <summary>
        /// Trace that the service is ready.
        /// </summary>
        /// <remarks>
        /// The call of this method  doesn't mean that it is fully initialized ready to process incoming requests.
        /// The service is fully ready only when <see cref="ServiceStarted"/> is called as well.
        /// </remarks>
        public static void ServiceReadyToProcessRequests(Context context, IClock? clock = null)
        {
            Trace(context, "CaSaaS is ready.");

            LifetimeTrackerHelper.Match(
                successAction: helper =>
                               {
                                   helper.Ready(clock.GetUtcNow());
                                   TraceIfReady(context, helper);
                               },
                failureAction: r => TraceHelperIsNotAvailable(context, r));
        }

        /// <summary>
        /// Trace that service initialization has failed.
        /// </summary>
        public static void ServiceStartupFailed(Context context, Exception exception, TimeSpan startupDuration)
        {
            Trace(context, $"CaSaaS initialization failed by {startupDuration.TotalMilliseconds}ms with error: {exception}");
        }

        /// <nodoc />
        public static void ShuttingDownService(Context context)
        {
            Trace(context, "Shutting down CaSaaS instance");
        }

        /// <nodoc />
        public static void ServiceStopped(Context context, BoolResult result)
        {
            var shutdownDuration = result.Duration;
            // Need to dispose the service tracker because the service has stopped.
            ServiceRunningTrackerResult.Match(
                successAction: r => r.Dispose(),
                failureAction: _ => { });

            ServiceRunningTrackerResult = ServiceRunningTrackerUnavailableResult;
            LifetimeTrackerHelper = LifetimeTrackerHelperUnavailableResult;

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

        private static void TraceIfReady(Context context, LifetimeTrackerHelper helper)
        {
            if (helper.IsFullyInitialized())
            {
                Trace(context, $"CaSaaS instance is fully initialized and ready to process requests. {helper}");
            }
        }

        private static void TraceHelperIsNotAvailable(Context context, Result<LifetimeTrackerHelper> helper, [CallerMemberName]string? caller = null)
        {
            Trace(context, $"Can't perform {caller} because {nameof(ServiceStartupFailed)} method was not called. Error={helper}.");
        }
        
        private static DateTime GetProcessStartupTimeUtc() => Process.GetCurrentProcess().StartTime.ToUniversalTime();

        private static void Trace(Context context, string message, Severity severity = Severity.Info, [CallerMemberName]string? operation = null)
        {
            context.TraceMessage(severity, message, ComponentName, operation);
        }
    }
}
