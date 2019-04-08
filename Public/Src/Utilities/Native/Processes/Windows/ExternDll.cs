// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Native.Processes.Windows
{
    /// <summary>
    /// Constants for names of various native libraries.
    /// </summary>
    public static class ExternDll
    {
#pragma warning disable CS1591 // Missing XML comment
        public const string Kernel32 = "KERNEL32.DLL";
        public const string DetoursServices32 = @"X86\DetoursServices.dll";
        public const string DetoursServices64 = @"X64\DetoursServices.dll";
        public const string BuildXLNatives64 = @"X64\BuildXLNatives.dll";
        public const string Psapi = "Psapi.dll";
        public const string Ntdll = "ntdll.dll";
        public const string Container = "container.dll";
        public const string Wcifs = "wci.dll";
        public const string Bindflt = "bindflt.dll";
        public const string Fltlib = "FltLib.dll";
        public const string Advapi32 = "advapi32.dll";
#pragma warning restore CS1591 // Missing XML comment
    }

}
