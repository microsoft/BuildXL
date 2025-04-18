// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Interop.Unix;
using BuildXL.Utilities.Core;

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
        public static string OsShellExe => OperatingSystemHelper.IsUnixOS ? UnixPaths.BinSh : s_cmdX64;

        /// <summary>
        /// File name of bash on unix
        /// </summary>
        public static string Bash => UnixPaths.BinBash;

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
                    UnixPaths.BinSh
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
                    UnixPaths.Bin,
                    UnixPaths.Sbin,
                    UnixPaths.Dev,
                    UnixPaths.UsrBin,
                    UnixPaths.UsrLib,
                    UnixPaths.Lib,
                    UnixPaths.Lib64,
                    UnixPaths.UsrLib64,
                    UnixPaths.Private,
                    UnixPaths.Etc,
                    UnixPaths.Proc,
                    UnixPaths.Var,
                    MacPaths.AppleInternal,
                    MacPaths.LibraryPreferencesLogging
                }
                : new string[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                })
                .Select(p => AbsolutePath.Create(pathTable, p));
        }

        /// <summary>
        /// Returns <see cref="GetCmdDependencies(PathTable)"/> formatted as a DScript array literal.
        /// </summary>
        public static string GetCmdDependenciesAsArrayLiteral(PathTable pathTable)
        {
            return "[ " + string.Join(", ", CmdHelper
                .GetCmdDependencies(pathTable)
                .Select(p => "f`" + p.ToString(pathTable) + "`")) + " ]";
        }

        /// <summary>
        /// Returns <see cref="GetCmdDependencyScopes(PathTable)"/> formatted as a DScript array literal.
        /// </summary>
        public static string GetOsShellDependencyScopesAsArrayLiteral(PathTable pathTable)
        {
            return "[ " + string.Join(", ", CmdHelper
                .GetCmdDependencyScopes(pathTable)
                .Select(p => "d`" + p.ToString(pathTable) + "`")) + " ]";
        }
    }
}
