// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Threading;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Native.Users;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// Helper class for checking running services.
    /// </summary>
    public sealed class ServiceReadinessChecker
    {
#if PLATFORM_WIN
        private readonly EventWaitHandle _readyEvent;
        private readonly EventWaitHandle _shutdownEvent;
#endif

        private readonly Tracer _tracer;

        /// <nodoc />
        public ServiceReadinessChecker(Tracer tracer, ILogger logger, string scenario)
        {
            _tracer = tracer;

#if PLATFORM_WIN
            _readyEvent = CreateReadyEvent(logger, scenario);
            _shutdownEvent = CreateShutdownEvent(logger, scenario);
#endif

        }

        /// <summary>
        ///     Check if the service is running.
        /// </summary>
        public static bool EnsureRunning(Context context, string scenario, int waitMs)
        {
            var currentUser = UserUtilities.CurrentUserName();

#if PLATFORM_WIN
            context.TraceMessage(Severity.Debug, $"Opening ready event name=[{scenario}] for user=[{currentUser}] waitMs={waitMs}.");
            var stopwatch = Stopwatch.StartNew();

            bool running = ensureRunningCore();

            if (running)
            {
                context.TraceMessage(Severity.Debug, $"Opened ready event name=[{scenario}] by {stopwatch.ElapsedMilliseconds}ms.");
            }

            return running;

            bool ensureRunningCore()
            {
                try
                {
                    if (!IpcUtilities.TryOpenExistingReadyWaitHandle(scenario, out EventWaitHandle readyEvent, waitMs))
                    {
                        context.TraceMessage(Severity.Debug, "Ready event does not exist");
                        return false;
                    }

                    using (readyEvent)
                    {
                        var waitMsRemaining = Math.Max(0, waitMs - (int)stopwatch.ElapsedMilliseconds);
                        if (!readyEvent.WaitOne(waitMsRemaining))
                        {
                            context.TraceMessage(Severity.Debug, "Ready event was opened but remained unset until timeout");
                            return false;
                        }

                        return true;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    context.TraceMessage(Severity.Debug, "Ready event exists, but user does not have acceptable security access");
                }

                return false;
            }

#else
            context.TraceMessage(Severity.Debug, $"Not validating ready event (OSX) name=[{scenario}] for user=[{currentUser}] waitMs={waitMs}");
            return true;
#endif
        }

        /// <summary>
        ///     Attempt to open event that will signal an imminent service shutdown or restart.
        /// </summary>
        public static EventWaitHandle OpenShutdownEvent(Context context, string scenario)
        {
            var currentUser = UserUtilities.CurrentUserName();
            context.TraceMessage(Severity.Debug, $"Opening shutdown event name=[{scenario}] for user=[{currentUser}]");

            EventWaitHandle handle = null;
            try
            {
                if (!IpcUtilities.TryOpenExistingShutdownWaitHandle(scenario, out handle))
                {
                    context.TraceMessage(Severity.Debug, "Shutdown event does not exist");
                }
            }
            catch (UnauthorizedAccessException)
            {
                context.TraceMessage(Severity.Debug, "Shutdown event exists, but user does not have acceptable security access");
            }

            return handle;
        }

        private static EventWaitHandle CreateReadyEvent(ILogger logger, string scenario)
        {
            var currentUser = UserUtilities.CurrentUserName();

            logger.Debug($"Creating ready event name=[{scenario}] for user=[{currentUser}]");
            var readyEvent = IpcUtilities.GetReadyWaitHandle(scenario);

            if (readyEvent.WaitOne(0))
            {
                readyEvent.Dispose();
                throw new CacheException("A service is already running");
            }

            return readyEvent;
        }

        private static EventWaitHandle CreateShutdownEvent(ILogger logger, string scenario)
        {
            var currentUser = UserUtilities.CurrentUserName();

            logger.Debug($"Creating shutdown event name=[{scenario}] for user=[{currentUser}]");
            var shutdownEvent = IpcUtilities.GetShutdownWaitHandle(scenario);

            if (shutdownEvent.WaitOne(0))
            {
                shutdownEvent.Dispose();
                throw new CacheException($"Shutdown event name=[{scenario}] already exists");
            }

            return shutdownEvent;
        }

        /// <summary>
        /// Sets readiness event.
        /// </summary>
        public void Ready(Context context)
        {
#if PLATFORM_WIN
            _tracer.Debug(context, "Setting ready event");
            _readyEvent.Set();
#endif
        }

        /// <summary>
        /// Resets readiness event.
        /// </summary>
        public void Reset()
        {
#if PLATFORM_WIN
            // Make sure to not signal new clients that service is ready for requests.
            _readyEvent.Reset();

            // Signal connected clients that shutdown is imminent.
            _shutdownEvent.Set();
#endif
        }

        /// <inheritdoc />
        public void Dispose()
        {
#if PLATFORM_WIN
            _shutdownEvent.Reset();
            _shutdownEvent.Dispose();
            _readyEvent.Dispose();
#endif
        }
    }
}
