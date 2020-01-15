// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;

namespace BuildXL.Cache.ContentStore.Hashing
{
    internal abstract class SafeHandleZeroOrMinusOneIsInvalid : SafeHandle
    {
        protected SafeHandleZeroOrMinusOneIsInvalid(bool ownsHandle) : base(IntPtr.Zero, ownsHandle)
        {
        }

        public override bool IsInvalid
        {
            get
            { return handle == IntPtr.Zero || handle == new IntPtr(-1); }
        }
    }
}
