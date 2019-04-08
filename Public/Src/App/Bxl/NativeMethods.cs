// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;

#if FEATURE_SAFE_PROCESS_HANDLE
using Microsoft.Win32.SafeHandles;
#endif

namespace BuildXL
{
    internal static class NativeMethods
    {
        [SuppressMessage("Microsoft.Usage", "CA2205:UseManagedEquivalentsOfWin32Api", Justification = "We want to check if native debugger is present as well.")]
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsDebuggerPresent();

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CheckRemoteDebuggerPresent(
#if FEATURE_SAFE_PROCESS_HANDLE
            SafeProcessHandle hProcess,
#else
            IntPtr hProcess,
#endif
            [MarshalAs(UnmanagedType.Bool)] ref bool isDebuggerPresent);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        internal static extern EXECUTION_STATE SetThreadExecutionState([In] EXECUTION_STATE esFlags);

        [Flags]
        internal enum EXECUTION_STATE : uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001,
            ES_USER_PRESENT = 0x00000004,
        }

        public static void LaunchDebuggerIfAttached()
        {
            bool isDebuggerAttached = false;
#if FEATURE_SAFE_PROCESS_HANDLE
            SafeProcessHandle hProcessHandle = Process.GetCurrentProcess().SafeHandle;
            Contract.Assert(!hProcessHandle.IsInvalid);
#else
            IntPtr hProcessHandle = Process.GetCurrentProcess().Handle;
            Contract.Assert(hProcessHandle != null);
#endif

            // CheckRemoteDebuggerPresent will determine if a remote debugger is attached.
            CheckRemoteDebuggerPresent(hProcessHandle, ref isDebuggerAttached);

            // Debugger.IsAttached will determine only if a managed debugger is attached.
            isDebuggerAttached = isDebuggerAttached || Debugger.IsAttached;

            // IsDebuggerPresent() will determine if a native debugger is attached.
            isDebuggerAttached = isDebuggerAttached || IsDebuggerPresent();

            if (isDebuggerAttached)
            {
                Debugger.Break();
            }
        }
    }
}
