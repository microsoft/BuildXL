// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using static BuildXL.Native.Processes.Windows.ProcessUtilitiesWin;

namespace BuildXL.Native.Processes.Windows
{
    /// <nodoc />
    public sealed class ProcessInjectorWin : IProcessInjector, IDisposable
    {
        [SuppressMessage("Microsoft.Reliability", "CA2006:UseSafeHandleToEncapsulateNativeResources")]
        private IntPtr m_injector;

        /// <nodoc />
        public ProcessInjectorWin(
            Guid payloadGuid,
            SafeHandle remoteInjectorPipe, SafeHandle reportPipe, string dllX86, string dllX64, ArraySegment<byte> payload)
        {
            var payloadPtr = IntPtr.Zero;
            var payloadHandle = default(GCHandle);
            var payloadSize = 0;
            if (payload.Array != null)
            {
                payloadHandle = GCHandle.Alloc(payload.Array, GCHandleType.Pinned);
                payloadPtr = payloadHandle.AddrOfPinnedObject();
                payloadPtr = IntPtr.Add(payloadPtr, payload.Offset);
                payloadSize = payload.Count;
            }

            Assert64Process();
            m_injector = DetouredProcessInjector_Create64(ref payloadGuid, remoteInjectorPipe, reportPipe, dllX86, dllX64, payloadSize, payloadPtr);

            if (payloadHandle.IsAllocated)
            {
                payloadHandle.Free();
            }
        }

        /// <inheritdoc />
        public bool IsDisposed => m_injector == IntPtr.Zero;

        /// <inheritdoc />
        public IntPtr Injector() => m_injector;

        /// <inheritdoc />
        public uint Inject(uint processId, bool inheritedHandles)
        {
            Assert64Process();
            return DetouredProcessInjector_Inject64(m_injector, processId, inheritedHandles);
        }

        /// <nodoc />
        public void Dispose()
        {
            InternalDispose();
            GC.SuppressFinalize(this);
        }

        private void InternalDispose()
        {
            // Dispose unmanaged resources
            if (!IsDisposed)
            {
                Assert64Process();
                DetouredProcessInjector_Destroy64(m_injector);

                m_injector = IntPtr.Zero;
            }
        }

        /// <nodoc />
        ~ProcessInjectorWin()
        {
            InternalDispose();
        }


        [SuppressMessage("Microsoft.Globalization", "CA2101:SpecifyMarshalingForPInvokeStringArguments")]
        [DllImport(ExternDll.BuildXLNatives64, EntryPoint = "DetouredProcessInjector_Create", SetLastError = true)]
        private static extern IntPtr DetouredProcessInjector_Create64(
            ref Guid payloadGuid,
            SafeHandle remoteInterjectorPipe,
            SafeHandle reportPipe,
            [MarshalAs(UnmanagedType.LPStr)]
            string dllX86,
            [MarshalAs(UnmanagedType.LPStr)]
            string dllX64,
            [MarshalAs(UnmanagedType.U4)]
            int payloadSize,
            IntPtr payload);

        [DllImport(ExternDll.BuildXLNatives64, EntryPoint = "DetouredProcessInjector_Destroy", SetLastError = true)]
        private static extern void DetouredProcessInjector_Destroy64(
            IntPtr injector);

        [DllImport(ExternDll.BuildXLNatives64, EntryPoint = "DetouredProcessInjector_Inject", CharSet = CharSet.Ansi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U4)]
        private static extern uint DetouredProcessInjector_Inject64(
            IntPtr injector,
            [MarshalAs(UnmanagedType.U4)]
            uint processId,
            [MarshalAs(UnmanagedType.Bool)]
            bool inheritedHandles);
    }
}

