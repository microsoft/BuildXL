// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Utilities;

using static BuildXL.Interop.MacOS.IO;

namespace Test.BuildXL.Processes
{
    /// <summary>
    /// Helper to run operating system's shell process pips
    /// </summary>
    public static class CmdHelper
    {
        private static readonly string s_cmdX64 = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        private static readonly string s_cmdX86 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "cmd.exe");
        private static readonly string s_conhost = Path.Combine(Environment.SystemDirectory, "conhost.exe");
        private static readonly string[] s_cmdExecutables = { s_cmdX64, s_cmdX86, s_conhost };

        /// <summary>
        /// File name of x64 cmd.exe
        /// </summary>
        public static string CmdX64 => s_cmdX64;

        /// <summary>
        /// File name of x86 cmd.exe
        /// </summary>
        public static string CmdX86 => s_cmdX86;

        /// <summary>
        /// File name of conhost.exe
        /// </summary>
        public static string Conhost => s_conhost;

        /// <summary>
        /// File name of the current operating system's shell executable
        /// </summary>
        public static string OsShellExe => OperatingSystemHelper.IsUnixOS ? BinSh : s_cmdX64;

        /// <summary>
        /// Gets list of files used by cmd.exe
        /// </summary>
        public static IEnumerable<AbsolutePath> GetCmdDependencies(PathTable pathTable)
        {
            Contract.Requires(pathTable != null);

            var assemblyDirName = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(typeof(CmdHelper).Assembly));

            // not sure why, but I observed the following during *some* test runs that involve cmd.exe
            return (OperatingSystemHelper.IsUnixOS
                ? new string[] 
                {
                    BinSh
                }
                : new string[]
                {
                    Path.Combine(assemblyDirName, "DetoursServices.pdb"),
                    Path.Combine(assemblyDirName, "BuildXLNatives.pdb"),
                })
                .Select(p => AbsolutePath.Create(pathTable, p));
        }

        /// <summary>
        /// Gets list of directories used by cmd.exe
        /// </summary>
        public static IEnumerable<AbsolutePath> GetCmdDependencyScopes(PathTable pathTable)
        {
            Contract.Requires(pathTable != null);
            return (OperatingSystemHelper.IsUnixOS
                ? new string[]
                {
                    Bin,
                    Dev,
                    UsrBin,
                    UsrLib,
                    Private,
                    Etc,
                    Var,
                    TmpDir,
                    AppleInternal
                }
                : new string[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                })
                .Select(p => AbsolutePath.Create(pathTable, p));
        }
    }
}
