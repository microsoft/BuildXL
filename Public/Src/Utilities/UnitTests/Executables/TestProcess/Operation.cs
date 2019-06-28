// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using BuildXL.Utilities;

namespace Test.BuildXL.Executables.TestProcess
{
    /// <summary>
    /// Defines allowable process operation and their behaviors
    /// </summary>
    public sealed class Operation
    {
        private const int NumArgsExpected = 6;
        private const int ERROR_ALREADY_EXISTS = 183;
        private const string StdErrMoniker = "stderr";
        private const string WaitToFinishMoniker = "wait";

        /// <summary>
        /// Returns an <code>IEnumerable</code> containing <paramref name="operation"/>
        /// <paramref name="count"/> number of times.
        /// </summary>
        public static IEnumerable<Operation> operator *(Operation operation, int count)
        {
            return Enumerable.Range(1, count).Select(_ => operation);
        }

        /*** PUBLIC DEFINITIONS ***/

        /// <summary>
        /// Input args delimiter between different fields of <see cref="Operation"/>
        /// </summary>
        public const char OperationArgsDelimiter = '?';

        /// <summary>
        /// Allowable filesystem operations test processes can execute
        /// </summary>
        public enum Type
        {
            /// <summary>
            /// Default no-op value
            /// Enum.TryParse will return default value on failure
            /// </summary>
            None,

            /// <summary>
            /// Type for creating a directory
            /// </summary>
            CreateDir,

            /// <summary>
            /// Type for deleting a file
            /// </summary>
            DeleteFile,

            /// <summary>
            /// Type for deleting a file with retry support
            /// </summary>
            DeleteFileWithRetries,

            /// <summary>
            /// Type for deleting a directory
            /// </summary>
            DeleteDir,

            /// <summary>
            /// Type for creating/writing a file
            /// </summary>
            WriteFile,

            /// <summary>
            /// Type for moving a file
            /// </summary>
            MoveFile,

            /// <summary>
            /// Write a file conditionally based on an input file
            /// </summary>
            WriteFileIfInputEqual,

            /// <summary>
            /// Type for creating/writing a file with retry support
            /// </summary>
            WriteFileWithRetries,

            /// <summary>
            /// Type for reading the content of a file and creating a file with the same content
            /// </summary>
            ReadAndWriteFile,

            /// <summary>
            /// Type for reading a file
            /// </summary>
            ReadFile,

            /// <summary>
            /// Type for reading a file (fails if a file does not exist)
            /// </summary>
            ReadRequiredFile,

            /// <summary>
            /// Type for copying a file
            /// </summary>
            CopyFile,

            /// <summary>
            /// Type for probing a file
            /// </summary>
            Probe,

            /// <summary>
            /// Type for enumerating a directory
            /// </summary>
            EnumerateDir,

            /// <summary>
            /// Type for creating a symlink to a file or directory
            /// </summary>
            CreateSymlink,

            /// <summary>
            /// Type for creating a hardlink
            /// </summary>
            CreateHardlink,

            /// <summary>
            /// Type for failing/crashing the test process
            /// </summary>
            Fail,

            /// <summary>
            /// Type for echoing a message
            /// </summary>
            Echo,

            /// <summary>
            /// Block the process indefinitely
            /// </summary>
            Block,

            /// <summary>
            /// Reads all lines from standard input and prints them out to standard output
            /// </summary>
            ReadStdIn,

            /// <summary>
            /// Echoes the current directory of the processes
            /// </summary>
            EchoCurrentDirectory,

            /// <summary>
            /// Spawns a child process
            /// </summary>
            Spawn,

            /// <summary>
            /// Like WriteFile with 'content' being Environment.NewLine
            /// </summary>
            AppendNewLine,

            /// <summary>
            /// Moves directory.
            /// </summary>
            MoveDir,

            /// <summary>
            /// Launches the debugger
            /// </summary>
            LaunchDebugger,

            /// <summary>
            /// Process that fails on first invocation and then succeeds on the second invocation
            /// </summary>
            SucceedOnRetry,
        }

        /// <summary>
        /// Whether a symbolic link leads to a file or a directory,
        /// as defined by WINAPI
        /// </summary>
        public enum SymbolicLinkFlag : uint
        {
            /// <summary>
            /// Arbitrary default that is meaningless to WINAPI call
            /// </summary>
            None = uint.MaxValue,

            /// <summary>
            /// Flag for creating a symbolic link to a file
            /// </summary>
            FILE = 0,

            /// <summary>
            /// Flag for creating a symbolic link to a directory
            /// </summary>
            DIRECTORY = 1,
        }

        // pinvoke functions (TODO: use PAL)
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateSymbolicLink")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExternWinCreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, SymbolicLinkFlag dwFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateHardLink")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExternWinCreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateDirectory")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExternWinCreateDirectory(string lpPathName, IntPtr lpSecurityAttributes);

        /*** STATE FOR BUILDXL TESTING ***/

        /// <summary>
        /// PathTable from surrounding Context to translate <see cref="FileOrDirectoryArtifact"/>s to strings
        /// </summary>
        public PathTable PathTable { get; set; }

        /// <summary>
        /// Whether or not the dependencies or outputs of this operation should be inferred, defaults to false.
        /// </summary>
        public bool DoNotInfer { get; set; }

        /*** STATE FOR FILESYSTEM OPERATIONS ***/

        /// <summary>
        /// Type of process (filesystem) operation
        /// </summary>
        public Type OpType { get; private set; }

#if TestProcess
        /// <summary>
        /// Should only be used in the context of TestProcess.exe
        /// </summary>
        private string PathAsString { get; set; }
#endif

        /// <summary>
        /// Path to file. NOTE: This will not be accessible within TestProcess.exe
        /// </summary>
        public FileOrDirectoryArtifact Path { get; private set; }

        /// <summary>
        /// Content to write to file
        /// </summary>
        public string Content { get; private set; }

#if TestProcess
        /// <summary>
        /// Should only be used in the context of TestProcess.exe
        /// </summary>
        private string LinkPathAsString { get; set; }
#endif

        /// <summary>
        /// Path of new symlink or hardlink to create. NOTE: This will not be accessible within TestProcess.exe
        /// </summary>
        public FileOrDirectoryArtifact LinkPath { get; private set; }

        /// <summary>
        /// Flag for correcting file or directory symlink
        /// </summary>
        public SymbolicLinkFlag SymLinkFlag { get; private set; }

        /// <summary>
        /// Arbitrary additional arguments that individual operations can interpret whichever way they want.
        ///
        /// Currently used to specify:
        ///   - search pattern for directory enumerations in <see cref="Type.EnumerateDir"/>
        ///   - whether to wait for the spawned process to finish in <see cref="Type.Spawn"/>
        ///   - whether to use stdout or stderr in <see cref="Type.Echo"/>
        /// </summary>
        public string AdditionalArgs { get; private set; }

        /// <summary>
        /// Number of retries when writing to a file
        /// </summary>
        public int RetriesOnWrite { get; }

        private Operation(Type type, FileOrDirectoryArtifact? path = null, string content = null, FileOrDirectoryArtifact? linkPath = null, SymbolicLinkFlag? symLinkFlag = null, bool? doNotInfer = null, string additionalArgs = null, int retriesOnWrite = 5)
        {
            Contract.Requires(content == null || !content.Contains(Environment.NewLine));

            RetriesOnWrite = retriesOnWrite;
            OpType = type;
            Path = path ?? FileOrDirectoryArtifact.Invalid;
            Content = content;
            LinkPath = linkPath ?? FileOrDirectoryArtifact.Invalid;
            SymLinkFlag = symLinkFlag ?? SymbolicLinkFlag.None;
            DoNotInfer = doNotInfer ?? true;
            AdditionalArgs = additionalArgs;
        }

#if TestProcess
        private static Operation FromCommandLine(Type type, string path = null, string content = null, string linkPath = null, SymbolicLinkFlag? symLinkFlag = null, string additionalArgs = null)
        {
            Contract.Requires(content == null || !content.Contains(Environment.NewLine));

            var result = new Operation(type: type,
                content: content,
                symLinkFlag: symLinkFlag,
                doNotInfer: false,
                additionalArgs: additionalArgs);
            result.PathAsString = path;
            result.LinkPathAsString = linkPath;

            return result;
        }
#endif

        /// <summary>
        /// Executes the associated filesystem operation
        /// </summary>
        public void Run()
        {
            // Make sure the string version of the paths are set in case the operation is run outside of the context of
            // TestProcess.exe
            if (LinkPath.IsValid)
            {
                LinkPathAsString = LinkPath.Path.ToString(PathTable);
            }
            if (Path.IsValid)
            {
                PathAsString = Path.Path.ToString(PathTable);
            }

            try
            {
                switch (OpType)
                {
                    case Type.None:
                        return;
                    case Type.CreateDir:
                        DoCreateDir();
                        return;
                    case Type.DeleteFile:
                        DoDeleteFile();
                        return;
                    case Type.DeleteDir:
                        DoDeleteDir();
                        return;
                    case Type.WriteFile:
                        DoWriteFile();
                        return;
                    case Type.ReadAndWriteFile:
                        DoReadAndWriteFile();
                        return;
                    case Type.ReadFile:
                        DoReadFile();
                        return;
                    case Type.ReadRequiredFile:
                        DoReadRequiredFile();
                        return;
                    case Type.CopyFile:
                        DoCopyFile();
                        return;
                    case Type.MoveDir:
                        DoMoveDir();
                        return;
                    case Type.Probe:
                        DoProbe();
                        return;
                    case Type.EnumerateDir:
                        DoEnumerateDir();
                        return;
                    case Type.CreateSymlink:
                        DoCreateSymlink();
                        return;
                    case Type.CreateHardlink:
                        DoCreateHardlink();
                        return;
                    case Type.WriteFileWithRetries:
                        DoOperationWithRetries(DoWriteFile);
                        return;
                    case Type.DeleteFileWithRetries:
                        DoOperationWithRetries(DoDeleteFile);
                        return;
                    case Type.Echo:
                        DoEcho();
                        return;
                    case Type.Block:
                        DoBlock();
                        return;
                    case Type.ReadStdIn:
                        DoReadStdIn();
                        return;
                    case Type.EchoCurrentDirectory:
                        DoEchoCurrentDirectory();
                        return;
                    case Type.Spawn:
                        DoSpawn();
                        return;
                    case Type.AppendNewLine:
                        DoWriteFile(Environment.NewLine);
                        return;
                    case Type.Fail:
                        DoFail();
                        return;
                    case Type.WriteFileIfInputEqual:
                        DoWriteFileIfInputEqual();
                        return;
                    case Type.LaunchDebugger:
                        Debugger.Launch();
                        return;
                    case Type.MoveFile:
                        DoMoveFile();
                        return;
                    case Type.SucceedOnRetry:
                        DoSucceedOnRetry();
                        return;
                }
            }
            catch (Exception e)
            {
                // Print error and exit with failure if any operation fails
                Console.Error.WriteLine("The test process failed to execute an operation: " + OpType.ToString());
                Console.Error.WriteLine(e);
                Environment.Exit(1);
            }
        }

        private const string EncodedStringListDelimeter = "%";

        private static string EncodeList(params string[] args)
        {
            return string.Join(EncodedStringListDelimeter, args.Select(arg => arg == null ? "~" : Convert.ToBase64String(Encoding.UTF8.GetBytes(arg))));
        }

        private static string[] DecodeList(string encoded)
        {
            return encoded.Split(EncodedStringListDelimeter[0]).Select(arg => arg == "~" ? null : Encoding.UTF8.GetString(Convert.FromBase64String(arg))).ToArray();
        }

        /*** FACTORY FUNCTIONS ***/

        /// <summary>
        /// When a message passed to the <see cref="Type.Echo"/> operation matches this regular expression,
        /// the echo operation extracts the 'VarName' block from the match, uses that value as an environment
        /// variable name, and prints out the value of that environment variable.
        /// </summary>
        public static readonly Regex EnvVarRegex = new Regex("^ENV{(?<VarName>.*)}$");

        /// <summary>
        /// Creates a create directory operation (uses WinAPI)
        /// </summary>
        public static Operation CreateDir(FileOrDirectoryArtifact path, bool doNotInfer = false, string additionalArgs = null)
        {
            return new Operation(Type.CreateDir, path, doNotInfer: doNotInfer, additionalArgs: additionalArgs);
        }

        /// <summary>
        /// Creates a write file operation that appends. The file is created if it does not exist.
        /// Writes random content to file at path if no content is specified.
        /// </summary>
        public static Operation WriteFile(FileArtifact path, string content = null, bool doNotInfer = false)
        {
            return content == Environment.NewLine
                ? new Operation(Type.AppendNewLine, path, doNotInfer: doNotInfer)
                : new Operation(Type.WriteFile, path, content, doNotInfer: doNotInfer);
        }

        /// <summary>
        /// Creates a read file operation followed by a write file operation. The content of the file that was read is used to
        /// write the new file.
        /// </summary>
        public static Operation ReadAndWriteFile(FileArtifact pathToRead, FileArtifact pathToWrite, bool doNotInfer = false)
        {
            return new Operation(Type.ReadAndWriteFile, pathToRead, content: null, pathToWrite, doNotInfer: doNotInfer);
        }

        /// <summary>
        /// Moves source to destination
        /// </summary>
        public static Operation MoveFile(FileArtifact source, FileArtifact destination, bool doNotInfer = false)
        {
            return new Operation(Type.MoveFile, source, content: null, linkPath: destination, doNotInfer: doNotInfer);
        }

        /// <summary>
        /// Creates a write file operation that appends. The file is created if it does not exist.
        /// Writes random content to file at path if no content is specified.
        /// </summary>
        public static Operation WriteFileIfInputEqual(FileArtifact path, string input, string value, string content = null)
        {
            return new Operation(Type.WriteFileIfInputEqual, path, EncodeList(input, value, content));
        }

        /// <summary>
        /// Creates a delete file operation
        /// </summary>
        public static Operation DeleteFile(FileArtifact path, bool doNotInfer = false)
        {
            return new Operation(Type.DeleteFile, path, content: null, doNotInfer: doNotInfer);
        }

        /// <summary>
        /// Creates a delete file operation with with the option to retry deleting to the file a specified number of times
        /// </summary>
        public static Operation DeleteFileWithRetries(FileArtifact path, bool doNotInfer = false, int retries = 5)
        {
            return new Operation(Type.DeleteFileWithRetries, path, content: null, doNotInfer: doNotInfer, retriesOnWrite: retries);
        }

        /// <summary>
        /// <see cref="WriteFile"/>, with the option to retry writing to the file a specified number of times
        /// </summary>
        public static Operation WriteFileWithRetries(FileArtifact path, string content = null, bool doNotInfer = false, int retries = 5)
        {
            Contract.Requires(retries >= 0);
            return new Operation(Type.WriteFileWithRetries, path, content, doNotInfer: doNotInfer, retriesOnWrite: retries);
        }

        /// <summary>
        /// Creates a read file operation
        /// </summary>
        public static Operation ReadFile(FileArtifact path, bool doNotInfer = false)
        {
            return new Operation(Type.ReadFile, path, doNotInfer: doNotInfer);
        }

        /// <summary>
        /// Creates a read file operation (fails if a file does not exist)
        /// </summary>
        public static Operation ReadRequiredFile(FileArtifact path, bool doNotInfer = false)
        {
            return new Operation(Type.ReadRequiredFile, path, doNotInfer: doNotInfer);
        }

        /// <summary>
        /// Creates a copy file operation
        /// </summary>
        public static Operation CopyFile(FileArtifact srcPath, FileArtifact destPath, bool doNotInfer = false)
        {
            return new Operation(Type.CopyFile, path: srcPath, linkPath: destPath, doNotInfer: doNotInfer);
        }

        /// <summary>
        /// Creates a move directory operation
        /// </summary>
        public static Operation MoveDir(DirectoryArtifact srcPath, DirectoryArtifact destPath)
        {
            return new Operation(Type.MoveDir, path: srcPath, linkPath: destPath, doNotInfer: true);
        }

        /// <summary>
        /// Creates a probe operation
        /// </summary>
        public static Operation Probe(FileOrDirectoryArtifact path, bool doNotInfer = false)
        {
            return new Operation(Type.Probe, path, doNotInfer: doNotInfer);
        }

        /// <summary>
        /// Creates a enumerate directory operation
        /// </summary>
        public static Operation EnumerateDir(DirectoryArtifact path, bool doNotInfer = false, string enumeratePattern = null)
        {
            return new Operation(Type.EnumerateDir, path, doNotInfer: doNotInfer, additionalArgs: enumeratePattern);
        }

        /// <summary>
        /// Creates a create symbolic link operation
        /// Requires process to run with elevated permissions for success
        /// </summary>
        public static Operation CreateSymlink(FileOrDirectoryArtifact linkPath, FileOrDirectoryArtifact targetPath, SymbolicLinkFlag symLinkFlag, bool doNotInfer = false)
        {
            return new Operation(Type.CreateSymlink, targetPath, linkPath: linkPath, symLinkFlag: symLinkFlag, doNotInfer: doNotInfer);
        }

        /// <summary>
        /// Creates a create symbolic link operation
        /// </summary>
        /// <remarks>
        /// This method is for testing symlinks whose target either
        /// (a) cannot be represented using FileArtifact/AbsolutePath (e.g., path contains an illegal char), or
        /// (b) should not be represented using these structs (e.g., target's path is a relative path)
        /// </remarks>
        public static Operation CreateSymlink(FileOrDirectoryArtifact linkPath, string target, SymbolicLinkFlag symLinkFlag, bool doNotInfer = false)
        {
            return new Operation(Type.CreateSymlink, linkPath: linkPath, additionalArgs: target, symLinkFlag: symLinkFlag, doNotInfer: doNotInfer);
        }

        /// <summary>
        /// Creates a create hard link operation.
        /// </summary>
        /// <param name="linkPath">Existing file</param>
        /// <param name="path">New file to create</param>
        /// <param name="doNotInfer">A flag for the PipTestBase class specifying whether to infer input/output dependencies</param>
        public static Operation CreateHardlink(FileArtifact linkPath, FileArtifact path, bool doNotInfer = false)
        {
            return new Operation(Type.CreateHardlink, path, linkPath: linkPath, doNotInfer: doNotInfer);
        }

        /// <summary>
        /// Creates an echo operation
        /// </summary>
        /// <param name="message">
        /// Message to echo.  If the value matches the <see cref="EnvVarRegex"/> regular expression,
        /// the value of the environment variable with that name is printed out instead.
        /// </param>
        /// <param name="useStdErr">If true: echo to standard error; else: echo to standard output.</param>
        public static Operation Echo(string message, bool useStdErr = false)
        {
            return new Operation(Type.Echo, content: message, additionalArgs: useStdErr ? StdErrMoniker : null);
        }

        /// <summary>
        /// Given an environment variable name (<paramref name="envVarName"/>) returns an
        /// <see cref="Type.Echo"/> operation that print out the value of that environment variable.
        /// </summary>
        public static Operation ReadEnvVar(string envVarName)
            => Echo(RenderGetEnvVarValueSyntax(envVarName));

        /// <summary>
        /// Creates a block operation that blocks the process indefinitely (<see cref="Type.Block"/>)
        /// </summary>
        public static Operation Block()
        {
            return new Operation(Type.Block);
        }

        /// <summary>
        /// Creates a read operation that reads all lines from standard input and prints them out to standard output.
        /// </summary>
        public static Operation ReadStdIn()
        {
            return new Operation(Type.ReadStdIn);
        }

        /// <summary>
        /// Creates an operation that echoes the current directory.
        /// </summary>
        public static Operation EchoCurrentDirectory()
        {
            return new Operation(Type.EchoCurrentDirectory);
        }

        /// <summary>
        /// Creates an operation that spawns a child process executing given <paramref name="childOperations"/>.
        /// </summary>
        /// <param name="pathTable">Needed for rendering child process operations</param>
        /// <param name="childOperations">Definition of the child process</param>
        /// <param name="waitToFinish">Whether to wait for the child process to finish before continuing</param>
        public static Operation Spawn(PathTable pathTable, bool waitToFinish, params Operation[] childOperations)
        {
            var args = childOperations.Select(o => (o.ToCommandLine(pathTable, escapeResult: true))).ToArray();
            return new Operation(Type.Spawn, content: EncodeList(args), additionalArgs: waitToFinish ? WaitToFinishMoniker : null);
        }

        /// <summary>
        /// Fails the process
        /// </summary>
        public static Operation Fail(int exitCode = -1)
        {
            return new Operation(Type.Fail, content: exitCode.ToString());
        }

        /// <summary>
        /// Process that fails on first invocation and succeeds on second invocation.
        /// </summary>
        /// <param name="untrackedStateFilePath">File used to track state. This path should be untracked when scheduling the pip</param>
        /// <param name="firstFailExitCode">Exit code for first failed invocation</param>
        /// <returns></returns>
        public static Operation SucceedOnRetry(FileArtifact untrackedStateFilePath, int firstFailExitCode = -1)
        {
            return new Operation(Type.SucceedOnRetry, path: untrackedStateFilePath, content: firstFailExitCode.ToString());
        }

        /// <summary>
        /// Launches the debugger
        /// </summary>
        public static Operation LaunchDebugger()
        {
            return new Operation(Type.LaunchDebugger);
        }

        /*** FILESYSTEM OPERATION FUNCTIONS ***/

        private void DoCreateDir()
        {
            bool failIfExists = false;

            if (!string.IsNullOrEmpty(AdditionalArgs))
            {
                string[] args = AdditionalArgs.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var arg in args)
                {
                    if (string.Equals(arg, "--failIfExists", StringComparison.OrdinalIgnoreCase))
                    {
                        failIfExists = true;
                    }
                }
            }

            if (FileUtilities.DirectoryExistsNoFollow(PathAsString) || FileUtilities.FileExistsNoFollow(PathAsString))
            {
                if (failIfExists)
                {
                    throw new InvalidOperationException($"Directory creation failed because '{PathAsString}' exists");
                }
            }

            if (OperatingSystemHelper.IsUnixOS)
            {
                Directory.CreateDirectory(PathAsString);
            }
            else
            {
                // .NET's Directory.CreateDirectory first checks for directory existence, so when a directory
                // exists it does nothing; to test a specific Detours access policy, we want to issue a "create directory"
                // operation regardless of whether the directory exists, hence p-invoking Windows native CreateDirectory.
                if (!ExternWinCreateDirectory(PathAsString, IntPtr.Zero))
                {
                    int errorCode = Marshal.GetLastWin32Error();

                    // If we got ERROR_ALREADY_EXISTS (183), keep it quiet --- yes we did not 'write' but the directory
                    // is on the disk after this method returns --- and do not fail the operation.
                    if (errorCode == ERROR_ALREADY_EXISTS)
                    {
                        return;
                    }

                    throw new InvalidOperationException($"Directory creation (native) for path '{PathAsString}' failed with error {errorCode}.");
                }
            }
        }

        private void DoDeleteFile()
        {
            File.Delete(PathAsString);
        }

        private void DoDeleteDir()
        {
            Directory.Delete(PathAsString);
        }

        // Writes random Content if Content not specified
        private void DoWriteFile()
        {
            DoWriteFile(Content ?? Guid.NewGuid().ToString());
        }

        private void DoReadAndWriteFile()
        {
            string content = DoReadFile();
            try
            {
                File.WriteAllText(LinkPathAsString, content == string.Empty ? Guid.NewGuid().ToString() : content);
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore tests for denied file access policies
            }
        }
        
        private void DoWriteFileIfInputEqual()
        {
            string[] argument = DecodeList(Content);
            string input = argument[0];
            string value = argument[1];
            string content = argument[2];

            if (File.ReadAllText(input) == value)
            {
                DoWriteFile(content ?? Guid.NewGuid().ToString());
            }
        }

        private void DoWriteFile(string content)
        {
            DoWriteFile(PathAsString, content);
        }

        private void DoWriteFile(string file, string content)
        {
            try
            {
                File.AppendAllText(file, content);
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore tests for denied file access policies
            }
        }

        private void DoOperationWithRetries(Action operation)
        {
            Content = Content ?? Guid.NewGuid().ToString();

            var retries = RetriesOnWrite;
            var sucess = false;
            while (!sucess || retries > 0)
            {
                try
                {
                    operation();
                    sucess = true;
                }
                catch (IOException)
                {
                    // This exception is thrown if two processes try to write to the same file
                    // So we put the thread to sleep for a small amount of (random) time
                    // so competing threads have their chance
                    Thread.Sleep(TimeSpan.FromMilliseconds(new Random().Next(1, 50)));
                }
                retries--;
            }
        }

        private string DoReadFile()
        {
            try
            {
                var content = File.ReadAllText(PathAsString);
                return content;
            }
            catch (FileNotFoundException)
            {
                // Ignore reading absent files
            }
            catch (DirectoryNotFoundException)
            {
                // Ignore reading absent paths
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore tests for denied file access policies
            }
            return string.Empty;
        }

        private void DoReadRequiredFile()
        {
            File.ReadAllText(PathAsString);
        }

        private void DoCopyFile()
        {
            File.Copy(PathAsString, LinkPathAsString, overwrite: true);
        }

        private void DoMoveDir()
        {
            if (Directory.Exists(LinkPathAsString))
            {
                Directory.Delete(LinkPathAsString, true);
            }

            Directory.Move(PathAsString, LinkPathAsString);
        }

        private void DoMoveFile()
        {
            File.Move(PathAsString, LinkPathAsString);
        }

        private void DoProbe()
        {
            File.Exists(PathAsString);
        }

        private void DoEnumerateDir()
        {
            try
            {
                string enumeratePattern = AdditionalArgs;
                if (enumeratePattern == null)
                {
                    Directory.EnumerateFileSystemEntries(PathAsString);
                }
                else
                {
                    Directory.EnumerateFileSystemEntries(PathAsString, enumeratePattern);
                }
            }
            catch (DirectoryNotFoundException)
            {
                // Ignore enumerating absent directories
            }
        }

        private void DoCreateSymlink()
        {
            string target = AdditionalArgs ?? PathAsString;

            // delete whatever might exist at the link location
            var linkPath = LinkPathAsString;
            FileUtilities.DeleteFile(linkPath);
            var maybeSymlink = FileUtilities.TryCreateSymbolicLink(linkPath, target, SymLinkFlag == SymbolicLinkFlag.FILE);
            if (!maybeSymlink.Succeeded)
            {
                throw maybeSymlink.Failure.CreateException();
            }
        }

        private void DoCreateHardlink()
        {
            var status = FileUtilities.TryCreateHardLink(PathAsString, LinkPathAsString);
            if (status != CreateHardLinkStatus.Success)
            {
                int error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException("Failed to create hard link at '" + PathAsString + "' pointing to '" + LinkPathAsString + "'. Error: " + error + ".");
            }
        }

        private static string ExtractVarNameFromEnvVarRegexMatch(Match m)
            => m.Groups["VarName"].Value;

        /// <summary>
        /// Given an environment variable name (<paramref name="envVarName"/>) returns a string which
        /// when passed to the <see cref="Type.Echo"/> operation makes the operation print out the value
        /// of that environment variable.
        /// </summary>
        private static string RenderGetEnvVarValueSyntax(string envVarName)
        {
            var result = $"ENV{{{envVarName}}}";
#if DEBUG
            // make sure that whatever we are about to return matches the regex used for parsing it
            var m = EnvVarRegex.Match(result);
            Contract.Assert(m.Success);
            Contract.Assert(envVarName == ExtractVarNameFromEnvVarRegexMatch(m));
#endif
            return result;
        }

        private void DoEcho()
        {
            var dest = (AdditionalArgs == StdErrMoniker)
                ? Console.Error
                : Console.Out;

            var match = EnvVarRegex.Match(Content);
            var message = match.Success
                ? Environment.GetEnvironmentVariable(ExtractVarNameFromEnvVarRegexMatch(match))
                : Content;

            dest.WriteLine(message);
        }

        private void DoBlock()
        {
            Console.WriteLine("Blocked forever");
            new ManualResetEventSlim().Wait();
        }

        private void DoReadStdIn()
        {
            string line;
            while ((line = Console.In.ReadLine()) != null)
            {
                Console.WriteLine(line);
            }
        }

        private void DoEchoCurrentDirectory()
        {
            Console.WriteLine(Environment.CurrentDirectory);
        }

        private void DoSpawn()
        {
            var cmdLine = string.Join(" ", DecodeList(Content));
            var waitToFinish = AdditionalArgs == WaitToFinishMoniker;

            var feedStdoutDone = new ManualResetEventSlim();
            var feedStderrDone = new ManualResetEventSlim();

            var process = new Process();
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += (sender, e) => FeedToOut(Console.Out, e.Data, feedStdoutDone);
            process.ErrorDataReceived += (sender, e) => FeedToOut(Console.Error, e.Data, feedStderrDone);
            process.StartInfo = new ProcessStartInfo
            {
                FileName = AssemblyHelper.GetAssemblyLocation(System.Reflection.Assembly.GetExecutingAssembly(), computeAssemblyLocation: true),
                Arguments = cmdLine,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (waitToFinish)
            {
                process.WaitForExit();
                feedStdoutDone.Wait();
                feedStderrDone.Wait();
            }

            void FeedToOut(TextWriter tw, string str, ManualResetEventSlim mre)
            {
                if (str == null)
                {
                    mre.Set();
                }
                else
                {
                    tw.WriteLine(str);
                }
            }
        }

        private void DoFail()
        {
            int exitCode = int.TryParse(Content, out var result) ? result : -1;
            Console.Error.WriteLine($"{Type.Fail} requested: Exiting with exit code {exitCode}");
            Environment.Exit(exitCode);
        }

        private void DoSucceedOnRetry()
        {
            // Use this state file to differentiate between the first run and the second run. The file will contain the exit code for the second run
            if (File.Exists(PathAsString))
            {
                int thisRunExitCode = int.Parse(File.ReadAllText(PathAsString));
                Environment.Exit(thisRunExitCode);
            }
            else
            {
                File.WriteAllText(PathAsString, "0");
                Environment.Exit(int.Parse(Content));
            }
        }

        /*** COMMAND LINE PARSING FUNCTION ***/

        /// <summary>
        /// Converts command line arg input to process ops
        /// Input format: "OpType?Path?Content?LinkPath?SymLinkFlag"
        /// </summary>
        /// <remarks>
        /// This and the execution of Operations within the context of TestProcess.exe should not utilize the PathTable
        /// since this adds an appreciable amount of JIT overhead to our test execution
        /// </remarks>
        public static Operation CreateFromCommandLine(string commandLineArg)
        {
            var opArgs = commandLineArg.Split(OperationArgsDelimiter);
            if (opArgs.Length != NumArgsExpected)
            {
                throw new ArgumentException(
                    string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    "An input argument (or delimiter) is missing. Expected {0} arguments, but received {1} arguments. Valid format is: OpType?Path?Content?LinkPath?SymLinkFlag. Raw command line is {2}", NumArgsExpected, opArgs.Length, commandLineArg));
            }

            Type opType;
            string pathAsString = "{Invalid}";
            SymbolicLinkFlag symLinkFlag;
            string linkPathAsString = "{Invalid}";

            if (!Enum.TryParse<Type>(opArgs[0], out opType))
            {
                throw new InvalidCastException("A malformed or invalid input argument was passed for the Operation type enum.");
            }

            if (opArgs[1].Length != 0)
            {
                pathAsString = opArgs[1];
            }

            string content = opArgs[2].Length == 0 ? null : opArgs[2];

            if (opArgs[3].Length != 0)
            {
                linkPathAsString = opArgs[3];
            }

            if (!Enum.TryParse<SymbolicLinkFlag>(opArgs[4], out symLinkFlag))
            {
                throw new InvalidCastException("A malformed or invalid input argument was passed for the Operation symbolic link flag enum.");
            }

            string additionalArgs = opArgs[5].Length == 0 ? null : opArgs[5];

            return Operation.FromCommandLine(
                type: opType, 
                path: pathAsString, 
                content: content, 
                linkPath: linkPathAsString, 
                symLinkFlag: symLinkFlag, 
                additionalArgs: additionalArgs);
        }

        /// <summary>
        /// Converts <see cref="Operation"/> to a well-formed string that can be used as command line input
        /// </summary>
        public string ToCommandLine(PathTable pathTable, bool escapeResult = false)
        {
            PathTable = pathTable;

            StringBuilder sb = new StringBuilder();

            sb.Append(((int)OpType).ToString(System.Globalization.CultureInfo.CurrentCulture));

            var orderOfArgs = new string[]
            {
                Path.IsValid ? FileOrDirectoryToString(Path) : null,
                Content,
                LinkPath.IsValid ? FileOrDirectoryToString(LinkPath) : null,
                SymLinkFlag.ToString(),
                AdditionalArgs
                // The process as a test executable does not use the DoNotInfer field
            };

            foreach (var arg in orderOfArgs)
            {
                sb.Append(OperationArgsDelimiter);
                if (arg != null)
                {
                    sb.Append(arg.ToString());
                }
            }

            return escapeResult
                ? CommandLineEscaping.EscapeAsCommandLineWord(sb.ToString())
                : sb.ToString();
        }

        /*** TO STRING HELPER FUNCTIONS ***/

        /// <summary>
        /// Converts FileOrDirectoryArtifact to string
        /// </summary>
        public string FileOrDirectoryToString(FileOrDirectoryArtifact fileArtifact)
        {
            return fileArtifact.Path.ToString(PathTable);
        }
    }
}
