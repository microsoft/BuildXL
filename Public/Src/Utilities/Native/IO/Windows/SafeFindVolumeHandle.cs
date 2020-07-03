// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
