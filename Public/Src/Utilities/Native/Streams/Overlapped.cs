// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;

namespace BuildXL.Native.Streams
{
    /// <summary>
    /// <c>OVERLAPPED</c> sturcture for async IO completion.
    /// See https://msdn.microsoft.com/en-us/library/windows/desktop/ms684342(v=vs.85).aspx
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct Overlapped
    {
        /// <summary>
        /// Internal completion state. Access via <c>GetQueuedCompletionStatus</c> or <c>GetOverlappedResult</c>.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2111:PointersShouldNotBeVisible")]
        public IntPtr InternalLow;

        /// <summary>
        /// Internal completion state. Access via <c>GetQueuedCompletionStatus</c> or <c>GetOverlappedResult</c>.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2111:PointersShouldNotBeVisible")]
        public IntPtr InternalHigh;

        /// <summary>
        /// Low part of the start offset (part of the I/O request).
        /// </summary>
        public uint OffsetLow;

        /// <summary>
        /// High part of the start offset (part of the I/O request).
        /// </summary>
        public uint OffsetHigh;

        /// <summary>
        /// Event handle to signal on completion. Not needed when using I/O completion ports exclusively.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2111:PointersShouldNotBeVisible")]
        public IntPtr EventHandle;

        /// <summary>
        /// Start offset (part of the I/O request).
        /// </summary>
        public long Offset
        {
            get
            {
                return checked((long)Bits.GetLongFromInts(OffsetHigh, OffsetLow));
            }

            set
            {
                OffsetLow = Bits.GetLowInt(checked((ulong)value));
                OffsetHigh = Bits.GetHighInt(checked((ulong)value));
            }
        }
    }
}
