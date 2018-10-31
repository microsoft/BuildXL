// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Win32.SafeHandles;

namespace BuildXL.Native.IO.Windows
{
    /// <summary>
    /// Handle for a volume iteration as returned by <see cref="FileSystemWin.FindFirstVolumeW(System.Text.StringBuilder, int)"/> />
    /// </summary>
    internal sealed class SafeFindVolumeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        /// Private constructor for the PInvoke marshaller.
        /// </summary>
        private SafeFindVolumeHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return FileSystemWin.FindVolumeClose(handle);
        }
    }
}
