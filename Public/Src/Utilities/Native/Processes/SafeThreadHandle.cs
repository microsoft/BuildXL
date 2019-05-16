// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Security;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Native.Processes
{
    /// <nodoc />
    [SuppressUnmanagedCodeSecurity]
    public sealed class SafeThreadHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        // constructor get's called by pinvoke
        private SafeThreadHandle()
            : base(true)
        {
        }

        /// <nodoc />
        protected override bool ReleaseHandle()
        {
            return IO.Windows.FileSystemWin.CloseHandle(handle);
        }
    }
}
