// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Reflection;
using BuildXL.Utilities.Core;

#nullable enable

namespace BuildXL.Processes
{
    /// <summary>
    /// Collections of binary paths used by the process detouring infrastructure.
    /// </summary>
    public sealed class BinaryPaths
    {
        /// <summary>
        /// The name of the detours services X64-bit DLL.
        /// </summary>
        public readonly string DllNameX64;

        /// <summary>
        /// The name of the detours services X86-bit DLL.
        /// </summary>
        public readonly string DllNameX86;

        /// <summary>
        /// Directory containing the X64-bit DLL.
        /// </summary>
        public readonly string DllDirectoryX64;

        /// <summary>
        /// Directory containing the X86-bit DLL.
        /// </summary>
        public readonly string DllDirectoryX86;

        /// <summary>
        /// Constructor.
        /// </summary>
        public BinaryPaths()
        {
            string directory = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(typeof(BinaryPaths).GetTypeInfo().Assembly)) ?? string.Empty;

            DllNameX86 = Path.Combine(directory, Native.Processes.Windows.ExternDll.DetoursServices32);
            DllNameX64 = Path.Combine(directory, Native.Processes.Windows.ExternDll.DetoursServices64);

            VerifyFileExists(DllNameX86);
            VerifyFileExists(DllNameX64);

            DllDirectoryX86 = Path.GetDirectoryName(DllNameX86) ?? string.Empty;
            DllDirectoryX64 = Path.GetDirectoryName(DllNameX64) ?? string.Empty;
        }

        private static void VerifyFileExists(string fileName)
        {
            if (!File.Exists(fileName))
            {
                throw new BuildXLException($"Cannot find file '{fileName}' needed to detour processes. Did you build all configurations?", rootCause: ExceptionRootCause.MissingRuntimeDependency);
            }
        }
    }
}
