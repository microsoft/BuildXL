// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Native.Users;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Helper class for handling inter-process communication.
    /// </summary>
    public static class IpcUtilities
    {
        private const string DefaultReadyEventName = @"Global\ContentServerReadyEvent";
        private const string DefaultShutdownEventName = @"Global\ContentServerShutdownEvent";

        /// <summary>
        /// Get the shutdown handle for the given scenario.
        /// </summary>
        /// <exception cref="CacheException"/>
        public static EventWaitHandle GetShutdownWaitHandle(string scenario)
        {
            try
            {
                string eventName = GetShutdownEventName(scenario);

                EventWaitHandle ret = new EventWaitHandle(false, EventResetMode.AutoReset, eventName, out bool created);

                var security = new EventWaitHandleSecurity();

                // Allow any client to wait on the event.
                security.AddAccessRule(EventWaitHandleAccessRules.PublicSynchronizeAccessRule());

                // Give full control to current user.
                security.AddAccessRule(EventWaitHandleAccessRules.CurrentUserFullControlRule());
                ret.SetAccessControl(security);

                return ret;
            }
            catch (Exception e)
            {
                throw new CacheException(e.ToString(), e);
            }
        }

        /// <summary>
        /// Get the ready handle for the given scenario.
        /// </summary>
        /// <exception cref="CacheException"/>
        public static EventWaitHandle GetReadyWaitHandle(string scenario)
        {
            try
            {
                var eventName = GetReadyEventName(scenario);

                var ret = new EventWaitHandle(false, EventResetMode.ManualReset, eventName, out bool created);

                var security = new EventWaitHandleSecurity();

                // Allow any client to wait on the event.
                security.AddAccessRule(EventWaitHandleAccessRules.PublicSynchronizeAccessRule());

                // Give full control to current user.
                security.AddAccessRule(EventWaitHandleAccessRules.CurrentUserFullControlRule());
                ret.SetAccessControl(security);

                return ret;
            }
            catch (Exception e)
            {
                throw new CacheException(e.ToString(), e);
            }
        }

        /// <summary>
        /// Try to get the ready handle for the given scenario.
        /// </summary>
        /// <exception cref="CacheException"/>
        public static bool TryOpenExistingReadyWaitHandle(string scenario, out EventWaitHandle readyEvent)
        {
            return TryOpenExistingReadyWaitHandle(scenario, out readyEvent, 0);
        }

        /// <summary>
        /// Try to get the ready handle for the given scenario within a given timeframe.
        /// </summary>
        /// <exception cref="CacheException"/>
        public static bool TryOpenExistingReadyWaitHandle(string scenario, out EventWaitHandle readyEvent, int waitMs)
        {
            var eventname = GetReadyEventName(scenario);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                while (!EventWaitHandle.TryOpenExisting(eventname, out readyEvent))
                {
                    if (stopwatch.ElapsedMilliseconds > waitMs)
                    {
                        return false;
                    }

                    Thread.Sleep(10);
                }

                return true;
            }
            catch (Exception e)
            {
                throw new CacheException(e.ToString(), e);
            }
        }

        /// <summary>
        /// Try to get the shutdown handle for the given scenario.
        /// </summary>
        /// <exception cref="CacheException"/>
        public static bool TryOpenExistingShutdownWaitHandle(string scenario, out EventWaitHandle handle)
        {
            try
            {
                return EventWaitHandle.TryOpenExisting(GetShutdownEventName(scenario), out handle);
            }
            catch (Exception e)
            {
                throw new CacheException(e.ToString(), e);
            }
        }

        /// <summary>
        /// Get the shutdown handle, set it, and finally close it.
        /// </summary>
        /// <exception cref="CacheException"/>
        public static bool SetShutdown(string scenario)
        {
            try
            {
                using (var handle = GetShutdownWaitHandle(scenario))
                {
                    return handle.Set();
                }
            }
            catch (Exception e)
            {
                throw new CacheException(e.ToString(), e);
            }
        }

        private static string GetReadyEventName(string scenario)
        {
            return string.IsNullOrEmpty(scenario)
                ? DefaultReadyEventName
                : DefaultReadyEventName + $"-{scenario}";
        }

        private static string GetShutdownEventName(string scenario)
        {
            return string.IsNullOrEmpty(scenario)
                ? DefaultShutdownEventName
                : DefaultShutdownEventName + $"-{scenario}";
        }

        private static class EventWaitHandleAccessRules
        {
            public static EventWaitHandleAccessRule PublicSynchronizeAccessRule()
            {
                var currentUser = UserUtilities.CurrentUserName();
                return new EventWaitHandleAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    EventWaitHandleRights.Synchronize,
                    AccessControlType.Allow);
            }

            public static EventWaitHandleAccessRule CurrentUserFullControlRule()
            {
                var currentUser = UserUtilities.CurrentUserName();
                return new EventWaitHandleAccessRule(
                    currentUser,
                    EventWaitHandleRights.FullControl,
                    AccessControlType.Allow);
            }
        }
    }
}
