// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !FEATURE_SAFE_PROCESS_HANDLE

using System;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Interop.Windows
{
    /// <nodoc />
    [SuppressUnmanagedCodeSecurity]
    public sealed class SafeProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        [DllImport(BuildXL.Interop.Libraries.WindowsKernel32, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        // constructor get's called by pinvoke
        private SafeProcessHandle()
            : base(true)
        {
        }

        /// <nodoc />
        protected override bool ReleaseHandle()
        {
            return CloseHandle(handle);
        }
    }
}
#endif
