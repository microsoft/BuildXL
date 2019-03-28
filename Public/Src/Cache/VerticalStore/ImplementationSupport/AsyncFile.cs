// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !FEATURE_SAFE_PROCESS_HANDLE
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using BuildXL.Utilities.Tasks;

#pragma warning disable 1591

//
// Code in this namespace is taken from VSO's code repo and included here until it can catch up with BuildXL via a normal code ingestion pipline.
//
namespace VSTS_Import
{
    public static class AsyncFile
    {
        private const int ERROR_IO_PENDING = 997;
        private const int ERROR_IO_DEVICE = 1117;

        private static readonly Task CompletedTask = Task.FromResult(0);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static unsafe extern int ReadFile(SafeFileHandle handle, byte* bytes, int numBytesToRead, IntPtr numBytesRead_mustBeZero, NativeOverlapped* overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static unsafe extern int WriteFile(SafeFileHandle handle, byte* bytes, int numBytesToWrite, IntPtr numBytesWritten_mustBeZero, NativeOverlapped* lpOverlapped);

        [Flags]
        private enum EMethod : uint
        {
            Buffered = 0,
        }

        [Flags]
        private enum EFileDevice : uint
        {
            FileSystem = 0x00000009,
        }

        /// <summary>
        /// IO Control Codes
        /// Useful links:
        ///     http://www.ioctls.net/
        ///     http://msdn.microsoft.com/en-us/library/windows/hardware/ff543023(v=vs.85).aspx
        /// </summary>
        [Flags]
        private enum EIOControlCode : uint
        {
            FsctlSetSparse = (EFileDevice.FileSystem << 16) | (49 << 2) | EMethod.Buffered | (0 << 14),
        }

        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            EIOControlCode ioControlCode,
            [MarshalAs(UnmanagedType.AsAny)]
            [In] object inBuffer,
            uint nInBufferSize,
            [MarshalAs(UnmanagedType.AsAny)]
            [Out] object outBuffer,
            uint nOutBufferSize,
            ref uint pBytesReturned,
            [In] IntPtr /*NativeOverlapped*/ overlapped);

        internal sealed class AsyncResult : IAsyncResult
        {
            public readonly Lazy<TaskSourceSlim<int>> CompletionSource =
                new Lazy<TaskSourceSlim<int>>(() => TaskSourceSlim.Create<int>());

            public bool IsCompleted { get { throw new NotSupportedException(); } }

            public WaitHandle AsyncWaitHandle { get { throw new NotSupportedException(); } }

            public object AsyncState { get { throw new NotSupportedException(); } }

            public bool CompletedSynchronously { get { throw new NotSupportedException(); } }
        }

        private unsafe static void CompletionCallback(uint errorCode, uint numBytes, NativeOverlapped* pOverlap)
        {
            try
            {
                Overlapped overlapped = Overlapped.Unpack(pOverlap);
                var asr = (AsyncResult)overlapped.AsyncResult;
                if (errorCode == 0)
                {
                    asr.CompletionSource.Value.SetResult((int)numBytes);
                }
                else
                {
                    asr.CompletionSource.Value.SetException(new Win32Exception((int)errorCode, $"Async file IO failed with {errorCode}"));
                }
            }
            finally
            {
                Overlapped.Free(pOverlap);
            }
        }

        public static int TryMarkSparse(SafeFileHandle hFile, bool sparse)
        {
            uint dwTemp = 0;
            short sSparse = sparse ? (short)1 : (short)0;
            if (DeviceIoControl(hFile, EIOControlCode.FsctlSetSparse, sSparse, 2, IntPtr.Zero, 0, ref dwTemp, IntPtr.Zero))
            {
                return 0;
            }

            return Marshal.GetLastWin32Error();
        }

        public static unsafe Task<int> ReadAsync(SafeFileHandle hFile, long fileOffset, byte[] buffer, int bytesToRead)
        {
            if (fileOffset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fileOffset));
            }

            if (bytesToRead < 0 || bytesToRead > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesToRead));
            }

            var asyncResult = new AsyncResult();
            var o = new Overlapped((int)(fileOffset & 0xFFFFFFFF), (int)(fileOffset >> 32), IntPtr.Zero, asyncResult);
            fixed (byte* bufferBase = buffer)
            {
                // https://docs.microsoft.com/en-us/dotnet/api/system.threading.overlapped.pack?view=netframework-4.7#System_Threading_Overlapped_Pack_System_Threading_IOCompletionCallback_System_Object_
                // The buffer or buffers specified in userData must be the same as those passed to the unmanaged operating system function that performs the asynchronous I/O. 
                // The runtime pins the buffer or buffers specified in userData for the duration of the I/O operation.
                NativeOverlapped* pOverlapped = o.Pack(CompletionCallback, buffer);
                bool needToFree = true;
                try
                {
                    if (ReadFile(hFile, bufferBase, bytesToRead, IntPtr.Zero, pOverlapped) != 0)
                    {
                        // Completed synchronously.

                        // The number of bytes transferred for the I/ O request.The system sets this member if the request is completed without errors.
                        // https://msdn.microsoft.com/en-us/library/windows/desktop/ms684342(v=vs.85).aspx
                        int bytesRead = (int)pOverlapped->InternalHigh.ToInt64();
                        return Task.FromResult(bytesRead);
                    }
                    else
                    {
                        int systemErrorCode = Marshal.GetLastWin32Error();
                        if (systemErrorCode == ERROR_IO_PENDING)
                        {
                            needToFree = false;
                        }
                        else
                        {
                            throw new Win32Exception(systemErrorCode, $"ReadFile failed with system error code:{systemErrorCode}");
                        }

                        return asyncResult.CompletionSource.Value.Task;
                    }
                }
                finally
                {
                    if (needToFree)
                    {
                        Overlapped.Unpack(pOverlapped);
                        Overlapped.Free(pOverlapped);
                    }
                }
            }
        }

        public static unsafe Task WriteAsync(SafeFileHandle hFile, long fileOffset, ArraySegment<byte> bytes)
        {
            if (fileOffset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fileOffset));
            }

            var asyncResult = new AsyncResult();
            int low = (int)(fileOffset & 0xFFFFFFFF);
            int high = (int)(fileOffset >> 32);
            var o = new Overlapped(low, high, IntPtr.Zero, asyncResult);
            fixed (byte* bufferBase = bytes.Array)
            {
                // https://docs.microsoft.com/en-us/dotnet/api/system.threading.overlapped.pack?view=netframework-4.7#System_Threading_Overlapped_Pack_System_Threading_IOCompletionCallback_System_Object_
                // The buffer or buffers specified in userData must be the same as those passed to the unmanaged operating system function that performs the asynchronous I/O. 
                // The runtime pins the buffer or buffers specified in userData for the duration of the I/O operation.
                NativeOverlapped* pOverlapped = o.Pack(CompletionCallback, bytes.Array);
                bool needToFree = true;
                try
                {
                    if (WriteFile(hFile, bufferBase + bytes.Offset, bytes.Count, IntPtr.Zero, pOverlapped) != 0)
                    {
                        // Completed synchronously.

                        // The number of bytes transferred for the I/O request. The system sets this member if the request is completed without errors.
                        // https://msdn.microsoft.com/en-us/library/windows/desktop/ms684342(v=vs.85).aspx
                        int bytesWritten = (int)pOverlapped->InternalHigh.ToInt64();
                        if (bytesWritten != bytes.Count)
                        {
                            throw new EndOfStreamException("Could not write all the bytes.");
                        }

                        return CompletedTask;
                    }
                    else
                    {
                        int systemErrorCode = Marshal.GetLastWin32Error();
                        if (systemErrorCode == ERROR_IO_DEVICE)
                        {
                            throw new IOException($"WriteFile failed with system error ERROR_IO_DEVICE", new Win32Exception(systemErrorCode));
                        }
                        else if (systemErrorCode == ERROR_IO_PENDING)
                        {
                            needToFree = false;
                        }
                        else
                        {
                            throw new Win32Exception(systemErrorCode, $"WriteFile failed with system error code:{systemErrorCode}");
                        }

                        return asyncResult.CompletionSource.Value.Task.ContinueWith(t =>
                        {
                            if (t.Status == TaskStatus.RanToCompletion && t.Result != bytes.Count)
                            {
                                throw new EndOfStreamException("Could not write all the bytes.");
                            }

                            return t;
                        });
                    }
                }
                finally
                {
                    if (needToFree)
                    {
                        Overlapped.Unpack(pOverlapped);
                        Overlapped.Free(pOverlapped);
                    }
                }
            }
        }
    }
}

#pragma warning restore 1591
#endif
