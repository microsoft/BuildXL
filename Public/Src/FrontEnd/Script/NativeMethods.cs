// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
