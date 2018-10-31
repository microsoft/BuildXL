// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace BuildXL.Native.IO.Windows
{
    /* wdm.h
    typedef struct _IO_STATUS_BLOCK {
            union {
                NTSTATUS Status;
                PVOID Pointer;
            } DUMMYUNIONNAME;

            ULONG_PTR Information;
        } IO_STATUS_BLOCK, *PIO_STATUS_BLOCK;
    */

    /// <summary>
    /// <c>IO_STATUS_BLOCK</c> as populated by various NT APIs.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct IoStatusBlock
    {
        /// <summary>
        /// Reserved. For internal use only.  Note that this is where the status is actually stored (like a union).
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2111:PointersShouldNotBeVisible")]
        public IntPtr Pointer;

        /// <summary>
        /// Note that this is not a pointer and should be treated instead as either a 32 or 64 bit ulong, depending on the platform.
        /// Call its ToInt32() or ToInt64() method to get its value.
        /// This is set to a request-dependent value. For example, on successful completion of a transfer request,
        /// this is set to the number of bytes transferred. If a transfer request is completed with
        /// another STATUS_XXX, this member is set to zero.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2111:PointersShouldNotBeVisible")]
        public IntPtr Information;

        /// <summary>
        /// Gets the completion status, either STATUS_SUCCESS if the requested operation was completed successfully
        /// or an informational, warning, or error STATUS_XXX value.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public NtStatus Status
        {
            get
            {
                return new NtStatus(unchecked((uint)Pointer.ToInt64()));
            }
        }
    }
}
