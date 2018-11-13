// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Win32.SafeHandles;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Native.IO.Windows
{
    /// <summary>
    /// Handle for a volume iteration as returned by <see cref="FileSystemWin.FindFirstVolumeW(System.Text.StringBuilder, int)"/> />
    /// </summary>
    public sealed class SafeFindFileHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        /// Private constructor for the PInvoke marshaller.
        /// </summary>
        private SafeFindFileHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return FileSystemWin.FindClose(handle);
        }
    }
}
