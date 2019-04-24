// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if FEATURE_ARIA_TELEMETRY

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// This class offers interop calls for remote telemetry stream reporting via the Aria C++ SDK on macOS
    /// </summary>
    public static class AriaMacOS
    {
        private const string AriaLibMacOS = "libBuildXLAria";

        /// <nodoc />
        [DllImport(AriaLibMacOS)]
        public static extern IntPtr CreateAriaLogger(string token, string db);

        /// <nodoc />
        [DllImport(AriaLibMacOS)]
        static public extern void DisposeAriaLogger(IntPtr logger);

        /// <nodoc />
        [DllImport(AriaLibMacOS)]
        static public extern IntPtr CreateEvent(string name);

        /// <nodoc />
        [DllImport(AriaLibMacOS)]
        static public extern void DisposeEvent(IntPtr event_);

        /// <nodoc />
        [DllImport(AriaLibMacOS)]
        static public extern void SetStringProperty(IntPtr event_, string name, string value);

        /// <nodoc />
        [DllImport(AriaLibMacOS)]
        static public extern void SetStringPropertyWithPiiKind(IntPtr event_, string name, string value, int pii);

        /// <nodoc />
        [DllImport(AriaLibMacOS)]
        static public extern void SetInt64Property(IntPtr event_, string name, long value);

        /// <nodoc />
        [DllImport(AriaLibMacOS)]
        static public extern void LogEvent(IntPtr logger, IntPtr event_);
    }
}
#endif //FEATURE_ARIA_TELEMETRY