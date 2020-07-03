// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace BuildXL.FrontEnd.Script
{
    internal static class NativeMethods
    {
        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", SetLastError = true)]
        public static extern IntPtr LoadLibraryW([In] [MarshalAs(UnmanagedType.LPWStr)] string lpLibFileName);

        [SuppressMessage("Microsoft.Globalization", "CA2101:SpecifyMarshalingForPInvokeStringArguments")]
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, EntryPoint = "GetProcAddress", SetLastError = true, ExactSpelling = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
    }
}
