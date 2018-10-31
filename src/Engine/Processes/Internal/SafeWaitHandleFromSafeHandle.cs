// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using Microsoft.Win32.SafeHandles;

#pragma warning disable SA1114 // Parameter list should follow declaration

namespace BuildXL.Processes.Internal
{
    internal sealed class SafeWaitHandleFromSafeHandle : WaitHandle
    {
        [ResourceExposure(ResourceScope.None)]
        [ResourceConsumption(ResourceScope.Machine, ResourceScope.Machine)]
        internal SafeWaitHandleFromSafeHandle(SafeHandle handle)
        {
            Contract.Requires(!handle.IsInvalid && !handle.IsClosed);

            SafeWaitHandle waitHandle;
            bool succeeded = ProcessUtilities.DuplicateHandle(
#if FEATURE_SAFE_PROCESS_HANDLE
                this.GetSafeWaitHandle(),
#else
                new HandleRef(this, ProcessUtilities.GetCurrentProcess()),
#endif
                handle,
#if FEATURE_SAFE_PROCESS_HANDLE
                this.GetSafeWaitHandle(),
#else
                new HandleRef(this, ProcessUtilities.GetCurrentProcess()),
#endif
                out waitHandle,
                0,
                false,
                ProcessUtilities.DUPLICATE_SAME_ACCESS);

            if (!succeeded)
            {
                throw new NativeWin32Exception(Marshal.GetLastWin32Error(), "Unable to duplicate a process handle for use as a wait handle.");
            }
#if FEATURE_SAFE_PROCESS_HANDLE
            this.SetSafeWaitHandle(waitHandle);
#else
            SafeWaitHandle = waitHandle;
#endif
        }
    }
}
