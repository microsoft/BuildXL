// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Processes.Internal
{
    internal static class Pipes
    {
        private const int PipeBufferSize = 2048;

        /// <summary>
        /// Indicates which sides of a new pipe should be marked as inheritable to child process.
        /// </summary>
        [Flags]
        public enum PipeInheritance
        {
            /// <summary>
            /// Neither read nor write sides are inheritable.
            /// </summary>
            None = 0,

            /// <summary>
            /// The read side is inheritable.
            /// </summary>
            InheritRead = 1,

            /// <summary>
            /// The write side is inheritable.
            /// </summary>
            InheritWrite = 2,

            /// <summary>
            /// Both the read and write sides are inheritable.
            /// </summary>
            InheritBoth = InheritRead | InheritWrite,
        }

        /// <summary>
        /// Flags controlling behavior of each end of the pipe. Setting a side 'async' is like specifying <c>FILE_FLAG_OVERLAPPED</c>
        /// for a normal file; doing so is required for dispatching completions to an IOCP.
        /// </summary>
        [Flags]
        public enum PipeFlags
        {
            /// <summary>
            /// No flags for either side.
            /// </summary>
            None,

            /// <summary>
            /// The read side of the pipe is configured for overlapped I/O.
            /// </summary>
            ReadSideAsync,

            /// <summary>
            /// The write side of the pipe is configured for overlapped I/O.
            /// </summary>
            WriteSideAsync,

            /// <summary>
            /// Both sides of the pipe are configured for overlapped I/O.
            /// </summary>
            BothSidesAsync = ReadSideAsync | WriteSideAsync,
        }

        /// <summary>
        /// Creates a pipe in which the specified ends are inheritable by child processes.
        /// In order for the pipe to be closable by the child, the inherited end should be closed after process creation.
        /// </summary>
        /// <remarks>
        /// This implementation creates a named pipe instance and immediately connects it. This is effectively the same implementation
        /// as <c>CreatePipe</c> (see %SDXROOT%\minkernel\kernelbase\pipe.c), but we compute a random pipe named rather than using the special
        /// 'anonymous' naming case of opening \\.\pipe or \Device\NamedPipe\ (without a subsequent path).
        /// We choose to not use <c>CreatePipe</c> since it opens both sides for synchronous I/O. Instead, for running build tools we want the
        /// BuildXL side opened for overlapped I/O and the build tool side opened for synchronous I/O (if the build tool side acts as a redirected
        /// console handle; recall that synchronous read and write attempts will fail on an async handle, which motivates the <c>CreatePipe</c> defaults
        /// in the first place). This is significant since many build tool console pipes are forever silent, and with sync handles we'd need N * 3 threads
        /// (stdin, stdout, stderr for N concurrent processes) to pessimistically drain them.
        /// </remarks>
        public static void CreateInheritablePipe(PipeInheritance inheritance, PipeFlags flags, out SafeFileHandle readHandle, out SafeFileHandle writeHandle)
        {
            string pipeName = @"\\.\pipe\BuildXL-" + Guid.NewGuid().ToString("N");

            writeHandle = ProcessUtilities.CreateNamedPipe(
                pipeName,
                PipeOpenMode.PipeAccessOutbound | (((flags & PipeFlags.WriteSideAsync) != 0) ? PipeOpenMode.FileFlagOverlapped : 0),
                PipeMode.PipeTypeByte | PipeMode.PipeRejectRemoteClients,
                nMaxInstances: 1,
                nOutBufferSize: PipeBufferSize,
                nInBufferSize: PipeBufferSize,
                nDefaultTimeout: 0,
                lpSecurityAttributes: IntPtr.Zero);

            if (writeHandle.IsInvalid)
            {
                throw new NativeWin32Exception(Marshal.GetLastWin32Error(), "CreateNamedPipeW failed");
            }

            var readSideFlags = FileFlagsAndAttributes.SecurityAnonymous;
            if ((flags & PipeFlags.ReadSideAsync) != 0)
            {
                readSideFlags |= FileFlagsAndAttributes.FileFlagOverlapped;
            }

            OpenFileResult openReadSideResult = FileUtilities.TryCreateOrOpenFile(
                pipeName,
                FileDesiredAccess.GenericRead,
                FileShare.None,
                FileMode.Open,
                readSideFlags,
                out readHandle);

            if (!openReadSideResult.Succeeded)
            {
                throw openReadSideResult.CreateExceptionForError();
            }

            if ((inheritance & PipeInheritance.InheritRead) != 0)
            {
                SetInheritable(readHandle);
            }

            if ((inheritance & PipeInheritance.InheritWrite) != 0)
            {
                SetInheritable(writeHandle);
            }
        }

        private static void SetInheritable(SafeHandle handle)
        {
            bool ret = ProcessUtilities.SetHandleInformation(handle, ProcessUtilities.HANDLE_FLAG_INHERIT, ProcessUtilities.HANDLE_FLAG_INHERIT);
            if (!ret)
            {
                throw new NativeWin32Exception(Marshal.GetLastWin32Error(), "SetHandleInformation");
            }
        }
    }
}
