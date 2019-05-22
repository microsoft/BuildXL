// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Native.Users;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Helper class for handling inter-process communication.
    /// </summary>
    public static class IpcUtilities
    {
        private const string DefaultReadyEventName = @"Global\ContentServerReadyEvent";
        private const string DefaultShutdownEventName = @"Global\ContentServerShutdownEvent";

        private const int ERROR_FILE_NOT_FOUND = 0x2;
        private const int ERROR_PATH_NOT_FOUND = 0x3;

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
                while (!EventWaitHandleTryOpenExisting(eventname, out readyEvent))
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
                return EventWaitHandleTryOpenExisting(GetShutdownEventName(scenario), out handle);
            }
            catch (Exception e)
            {
                throw new CacheException(e.ToString(), e);
            }
        }

        [DllImport("kernel32", EntryPoint = "OpenEventW", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeWaitHandle OpenEvent(uint desiredAccess, bool inheritHandle, string name);

        private static ConstructorInfo EventWaitHandleConstructor = typeof(EventWaitHandle).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(SafeWaitHandle) },
            null);

        private static bool EventWaitHandleTryOpenExisting(string name, out EventWaitHandle handle)
        {
            // In .NET Core, only EventWaitHandle.TryOpenExisting(name, out handle) is supported,
            // which requires both Synchronize and Modify rights on the handle
            // (see: https://github.com/dotnet/corefx/blob/b86783ef9525f22b0576278939264897464f9bbd/src/Common/src/CoreLib/System/Threading/EventWaitHandle.Windows.cs#L14)
            //
            // When we create this handle, we grant only Synchronize, which is why here we have
            // to work around this issue by directly p-invoking OpenEvent from kernel32.dll.
#if FEATURE_CORECLR
            handle = null;
            SafeWaitHandle myHandle = OpenEvent((uint)EventWaitHandleRights.Synchronize, false, name);
            if (myHandle.IsInvalid)
            {
                var errorCode = Marshal.GetLastWin32Error();
                if (errorCode == ERROR_FILE_NOT_FOUND || errorCode == ERROR_PATH_NOT_FOUND)
                    return false;
                throw Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error());
            }

            handle = (EventWaitHandle) EventWaitHandleConstructor.Invoke(new[] { myHandle });
            return true;
#else
            return EventWaitHandle.TryOpenExisting(name, EventWaitHandleRights.Synchronize, out handle);
#endif
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
