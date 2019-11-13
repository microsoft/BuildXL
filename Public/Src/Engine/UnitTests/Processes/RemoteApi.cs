// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;

namespace Test.BuildXL.Processes
{
    /// <summary>
    /// Support for running the <c>RemoteApi.exe</c> test program as a <see cref="SandboxedProcess" />.
    /// <c>RemoteApi.exe</c> exposes some key file system APIs or patterns (e.g. enumeration with FindFirstFile)
    /// over RPC, to facilitate targeted testing of Detours reporting and enforcement.
    /// </summary>
    public static class RemoteApi
    {
        /// <summary>
        /// Path to <c>RemoteApi.exe</c>
        /// </summary>
        public static readonly string ExecutablePath = GetRemoteApiExeLocation();

        /// <summary>
        /// Runs a sequence of RemoteApi commands in a <see cref="SandboxedProcess" />.
        /// </summary>
        public static async Task<SandboxedProcessResult> RunInSandboxAsync(
            PathTable pathTable,
            string workingDirectory,
            ISandboxedProcessFileStorage sandboxStorage,
            Action<FileAccessManifest> populateManifest,
            params Command[] commands)
        {
            Contract.Requires(!string.IsNullOrEmpty(workingDirectory));
            Contract.Requires(populateManifest != null);

            if (!File.Exists(ExecutablePath))
            {
                throw new BuildXLException("Expected to find RemoteApi.exe at " + ExecutablePath);
            }

            var info =
                new SandboxedProcessInfo(pathTable, sandboxStorage, ExecutablePath, disableConHostSharing: false)
                {
                    PipSemiStableHash = 0,
                    PipDescription = "RemoteApi Test",
                    Arguments = string.Empty,
                    WorkingDirectory = workingDirectory,
                };

            info.FileAccessManifest.ReportFileAccesses = false;
            info.FileAccessManifest.ReportUnexpectedFileAccesses = true;
            info.FileAccessManifest.FailUnexpectedFileAccesses = false;

            info.FileAccessManifest.AddScope(AbsolutePath.Invalid, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportDirectoryEnumerationAccess);

            populateManifest(info.FileAccessManifest);

            // Allow access to the RemoteApi executable.
            AbsolutePath exeDirectory = AbsolutePath.Create(pathTable, Path.GetDirectoryName(ExecutablePath));
            info.FileAccessManifest.AddScope(exeDirectory, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowReadAlways);

            using (TextReader commandReader = GetCommandReader(commands))
            {
                info.StandardInputReader = commandReader;
                info.StandardInputEncoding = Encoding.ASCII;

                // TODO: Maybe watch stdout and validate the command results.
                using (SandboxedProcess process = await SandboxedProcess.StartAsync(info))
                {
                    SandboxedProcessResult result = await process.GetResultAsync();
                    if (result.ExitCode != 0)
                    {
                        var stdErr = await result.StandardError.ReadValueAsync();
                        XAssert.AreEqual(0, result.ExitCode, "RemoteApi.exe failed: " + stdErr);
                    }

                    return result;
                }
            }
        }

        private static TextReader GetCommandReader(Command[] commands)
        {
            var commandBuffer = new StringBuilder();
            foreach (Command command in commands)
            {
                commandBuffer.AppendLine(command.Serialize());
            }

            return new StringReader(commandBuffer.ToString());
        }

        private static string GetRemoteApiExeLocation()
        {
            string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
            currentCodeFolder = Path.Combine(currentCodeFolder, "DetoursCrossBitTests");
            Contract.Assume(currentCodeFolder != null);
            return Path.GetFullPath(Path.Combine(currentCodeFolder, "x64", "RemoteApi.exe"));
        }

        /// <summary>
        /// Command to run.
        /// </summary>
        public enum CommandType
        {
            /// <summary>
            /// Enumerates a specified directory with FindFirstFileEx / FindNextFile.
            /// The parameter is a path with an optional wildcard in the last component (the primary parameter to FindFirstFileEx).
            /// </summary>
            EnumerateWithFindFirstFileEx,

            /// <summary>
            /// Opens a specified path, and enumerates it (by handle) with NtQueryDirectoryFile.
            /// </summary>
            EnumerateFileOrDirectoryByHandle,

            /// <summary>
            /// Opens and deletes a file with NtCreateFile.
            /// The parameter is a canonicalized path to delete.
            /// </summary>
            DeleteViaNtCreateFile,

            /// <summary>
            /// Creates a new hardlink via <c>CreateHardLinkW</c>
            /// </summary>
            CreateHardLink,
        }

        /// <summary>
        /// Single command to send to the Remote API program.
        /// </summary>
        public sealed class Command
        {
            /// <summary>
            /// Command to run.
            /// </summary>
            public readonly CommandType CommandType;

            /// <summary>
            /// Command parameter 1 (often a path).
            /// </summary>
            public readonly string Parameter1;

            /// <summary>
            /// Command parameter 2 (often a path).
            /// </summary>
            public readonly string Parameter2;

            /// <nodoc />
            public Command(CommandType commandType, string parameter1, string parameter2 = null)
            {
                Contract.Requires(parameter1 != null);
                Parameter1 = parameter1;
                Parameter2 = parameter2;
                CommandType = commandType;
            }

            internal string Serialize()
            {
                if (Parameter2 == null)
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0:G},{1}", CommandType, Parameter1, Parameter2);
                }
                else
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0:G},{1},{2}", CommandType, Parameter1, Parameter2);
                }
            }

            /// <nodoc />
            public static Command EnumerateWithFindFirstFileEx(string path)
            {
                return new Command(CommandType.EnumerateWithFindFirstFileEx, path);
            }

            /// <nodoc />
            public static Command EnumerateFileOrDirectoryByHandle(string path)
            {
                return new Command(CommandType.EnumerateFileOrDirectoryByHandle, path);
            }

            /// <nodoc />
            public static Command DeleteViaNtCreateFile(string absolutePath)
            {
                return new Command(CommandType.DeleteViaNtCreateFile, @"\??\" + absolutePath);
            }

            /// <nodoc />
            public static Command CreateHardlink(string existingFile, string newLink)
            {
                return new Command(CommandType.CreateHardLink, existingFile, newLink);
            }
        }
    }
}
