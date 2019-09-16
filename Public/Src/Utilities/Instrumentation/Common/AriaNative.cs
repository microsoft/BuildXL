// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// This class offers interop calls for remote telemetry stream reporting via the Aria C++ SDK
    /// </summary>
    public static class AriaNative
    {
#if PLATFORM_WIN
        private const string AriaLibName = "x64\\BuildXLAria";
#else
        private const string AriaLibName = "libBuildXLAria";
#endif

        /// <nodoc />
        [StructLayout(LayoutKind.Sequential)]
        public struct EventProperty
        {
            /// <nodoc />
            [MarshalAs(UnmanagedType.LPStr)]
            public string Name;

            /// <nodoc />
            [MarshalAs(UnmanagedType.LPStr)]
            public string Value;

            /// <nodoc />
            public long PiiOrValue;
        }

        /// <nodoc />
        [DllImport(AriaLibName)]
        public static extern int Get42();

        /// <nodoc />
        [DllImport(AriaLibName)]
        public static extern IntPtr CreateAriaLogger(
            [MarshalAs(UnmanagedType.LPStr)] string token,
            [MarshalAs(UnmanagedType.LPStr)] string db,
            int teardownTimeoutInSeconds);

        /// <nodoc />
        [DllImport(AriaLibName)]
        public static extern void DisposeAriaLogger(IntPtr logger);

        /// <nodoc />
        public static void LogEvent(IntPtr logger, string eventName, EventProperty[] eventProperties)
        {
            ExternLogEvent(logger, eventName, eventProperties.Length, eventProperties);
        }

        [DllImport(AriaLibName, EntryPoint = "LogEvent")]
        private static extern void ExternLogEvent(
            IntPtr logger,
            [MarshalAs(UnmanagedType.LPStr)] string eventName,
            int eventPropertiesLength,
            [MarshalAs(UnmanagedType.LPArray)] EventProperty[] eventProperties);
    }
}
