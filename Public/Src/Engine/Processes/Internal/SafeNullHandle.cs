// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Processes.Internal
{
    [SuppressUnmanagedCodeSecurity]
    internal sealed class SafeNullHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeNullHandle()
            : base(false)
        {
        }

        public static readonly SafeHandle Instance = new SafeNullHandle();

        protected override bool ReleaseHandle()
        {
            return false;
        }
    }
}
