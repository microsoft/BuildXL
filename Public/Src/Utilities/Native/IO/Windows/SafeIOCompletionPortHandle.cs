// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Security;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Native.IO.Windows
{
    /// <summary>
    /// Handle for an IO completion port as created by <see cref="FileSystemWin.CreateIOCompletionPort"/>.
    /// </summary>
    [SuppressUnmanagedCodeSecurity]
    public sealed class SafeIOCompletionPortHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        /// Private constructor for the pinvoke marshaler.
        /// </summary>
        private SafeIOCompletionPortHandle()
            : base(true)
        {
        }

        /// <summary>
        /// Creates a safe handle from the given, not-yet-wrapped native handle. This handle must not yet be owned by another safe handle instance.
        /// </summary>
        public SafeIOCompletionPortHandle(IntPtr nativeHandle)
            : base(ownsHandle: true)
        {
            SetHandle(nativeHandle);
        }

        /// <summary>
        /// Returns a safe handle instance representing a null / invalid handle.
        /// </summary>
        public static SafeIOCompletionPortHandle CreateInvalid()
        {
            return new SafeIOCompletionPortHandle();
        }

        /// <inheritdoc />
        protected override bool ReleaseHandle()
        {
            return FileSystemWin.CloseHandle(handle);
        }
    }
}
