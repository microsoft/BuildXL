// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using BuildXL.Processes.Internal;
using Microsoft.Win32.SafeHandles;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Processes
{
    /// <summary>
    /// Tests verifying the Win32 <c>HANDLE_FLAG_INHERIT</c> bit produced by <see cref="Pipes.CreateInheritablePipe"/>
    /// and <see cref="Pipes.CreateNamedPipeServerStream"/>. These are regression guards for the report-pipe / injector-pipe
    /// inheritance leak that caused the long <c>SandboxedProcess.WaitUntilReportEofAsync</c> stalls observed in production
    /// Bothell builds.
    /// </summary>
    public sealed class PipesInheritanceTests : XunitBuildXLTest
    {
        private const int HANDLE_FLAG_INHERIT = 0x00000001;

        public PipesInheritanceTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void CreateNamedPipeServerStream_TrueMarksClientInheritable()
        {
            // Regression guard: markClientHandleInheritable=true must produce an inheritable client handle (used by
            // stdin/stdout/stderr redirection in DetouredProcess.cs and by callers that rely on Win32 inheritance).
            using NamedPipeServerStream pipeStream = Pipes.CreateNamedPipeServerStream(
                PipeDirection.In,
                PipeOptions.Asynchronous,
                PipeOptions.None,
                out SafeFileHandle clientHandle,
                markClientHandleInheritable: true);
            using (clientHandle)
            {
                XAssert.IsTrue(IsInheritable(clientHandle), "markClientHandleInheritable=true must leave HANDLE_FLAG_INHERIT set.");
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void CreateNamedPipeServerStream_FalseSkipsInheritable()
        {
            // The fix: passing markClientHandleInheritable=false must NOT set HANDLE_FLAG_INHERIT on the client handle.
            using NamedPipeServerStream pipeStream = Pipes.CreateNamedPipeServerStream(
                PipeDirection.In,
                PipeOptions.Asynchronous,
                PipeOptions.None,
                out SafeFileHandle clientHandle,
                markClientHandleInheritable: false);
            using (clientHandle)
            {
                XAssert.IsFalse(IsInheritable(clientHandle), "markClientHandleInheritable=false should leave HANDLE_FLAG_INHERIT clear.");
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void CreateInheritablePipe_None_LeavesBothHandlesNonInheritable()
        {
            // The fix: PipeInheritance.None must produce no HANDLE_FLAG_INHERIT on either end (the non-managed pipe-reader path
            // for both the report-pipe and the injector-pipe relies on this).
            Pipes.CreateInheritablePipe(
                Pipes.PipeInheritance.None,
                Pipes.PipeFlags.ReadSideAsync,
                out SafeFileHandle readHandle,
                out SafeFileHandle writeHandle);

            using (readHandle)
            using (writeHandle)
            {
                XAssert.IsFalse(IsInheritable(readHandle), "PipeInheritance.None must leave the read handle non-inheritable.");
                XAssert.IsFalse(IsInheritable(writeHandle), "PipeInheritance.None must leave the write handle non-inheritable.");
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void CreateInheritablePipe_InheritWrite_OnlyWriteIsInheritable()
        {
            // Regression guard for stdin/stdout/stderr callers in DetouredProcess.cs: PipeInheritance.InheritWrite must still
            // mark just the write side inheritable.
            Pipes.CreateInheritablePipe(
                Pipes.PipeInheritance.InheritWrite,
                Pipes.PipeFlags.ReadSideAsync,
                out SafeFileHandle readHandle,
                out SafeFileHandle writeHandle);

            using (readHandle)
            using (writeHandle)
            {
                XAssert.IsFalse(IsInheritable(readHandle), "PipeInheritance.InheritWrite must leave the read handle non-inheritable.");
                XAssert.IsTrue(IsInheritable(writeHandle), "PipeInheritance.InheritWrite must mark the write handle inheritable.");
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void CreateInheritablePipe_InheritRead_OnlyReadIsInheritable()
        {
            // Regression guard for the stdin redirection path in DetouredProcess.cs (mirrors the InheritWrite case for stdout/stderr).
            Pipes.CreateInheritablePipe(
                Pipes.PipeInheritance.InheritRead,
                Pipes.PipeFlags.WriteSideAsync,
                out SafeFileHandle readHandle,
                out SafeFileHandle writeHandle);

            using (readHandle)
            using (writeHandle)
            {
                XAssert.IsTrue(IsInheritable(readHandle), "PipeInheritance.InheritRead must mark the read handle inheritable.");
                XAssert.IsFalse(IsInheritable(writeHandle), "PipeInheritance.InheritRead must leave the write handle non-inheritable.");
            }
        }

        private static bool IsInheritable(SafeFileHandle handle)
        {
            if (!GetHandleInformation(handle, out uint flags))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetHandleInformation");
            }

            return (flags & HANDLE_FLAG_INHERIT) != 0;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetHandleInformation(SafeFileHandle hObject, out uint lpdwFlags);
    }
}
