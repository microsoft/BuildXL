// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Processes;
using Test.BuildXL.TestUtilities;

namespace DetoursCrossBitTests
{
    public static class Program
    {
        private const string WrittenText = "Hello";
        private const string TestExecutableX64 = "DetoursCrossBitTests-X64.exe";
        private const string TestExecutableX86 = "DetoursCrossBitTests-X86.exe";

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public static int Main(string[] args)
        {
            ContentHashingUtilities.SetDefaultHashType();

            try
            {
                if (args.Length < 1)
                {
                    return -1;
                }

                string tempDirectory;
                string outFile;
                bool outFileSpecified;

                if (!args[args.Length - 1].StartsWith("/out:", StringComparison.OrdinalIgnoreCase))
                {
                    outFileSpecified = false;
                    tempDirectory = CreateTempDirectory();
                    outFile = GetFileName(tempDirectory);
                }
                else
                {
                    outFileSpecified = true;
                    outFile = args[args.Length - 1].Substring("/out:".Length);
                    tempDirectory = Path.GetDirectoryName(outFile);
                }

                string instruction = args[0];
                var restInstructions = new List<string>(args.Skip(1));
                if (!outFileSpecified)
                {
                    restInstructions.Add("/out:" + outFile);
                }

                Func<string, bool> equalInstruction = s => string.Equals(instruction, s, StringComparison.OrdinalIgnoreCase);
                Task<bool> retVal;

                if (equalInstruction("cmd32"))
                {
                    retVal = CreateAndRunPip(PipProgram.Cmd, tempDirectory, outFile, restInstructions, false);
                }
                else if (equalInstruction("cmd64"))
                {
                    retVal = CreateAndRunPip(PipProgram.Cmd, tempDirectory, outFile, restInstructions, true);
                }
                else if (equalInstruction("self32"))
                {
                    retVal = CreateAndRunPip(PipProgram.Self, tempDirectory, outFile, restInstructions, false);
                }
                else if (equalInstruction("self64"))
                {
                    retVal = CreateAndRunPip(PipProgram.Self, tempDirectory, outFile, restInstructions, true);
                }
                else if (equalInstruction("selfChild32"))
                {
                    retVal = CreateSelfChild(restInstructions.ToArray(), false);
                }
                else if (equalInstruction("selfChild64"))
                {
                    retVal = CreateSelfChild(restInstructions.ToArray(), true);
                }
                else
                {
                    retVal = !string.IsNullOrEmpty(outFile) ? WriteToFile(outFile) : BoolTask.False;
                }

                int result = retVal.Result ? 0 : 1;

                if (!outFileSpecified && Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    "Failure in cross-bit executable with command line {0}:\n{1}\n{2}",
                    Environment.CommandLine,
                    ex.Message,
                    ex.StackTrace);
                if (ex.InnerException != null)
                {
                    Console.Error.WriteLine(
                        "InnerException: {0}\n{1}",
                        ex.InnerException.Message,
                        ex.InnerException.StackTrace);
                }

                throw;
            }
        }

        private enum PipProgram
        {
            Cmd,
            Self,
        }

        private static Task<bool> WriteToFile(string path)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));

            File.WriteAllText(path, WrittenText);
            return BoolTask.True;
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        [SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        private static Task<bool> CreateSelfChild(string[] restInstruction, bool is64Bit)
        {
            Contract.Requires(restInstruction != null);

            bool retVal;
            string executable = is64Bit ? TestExecutableX64 : TestExecutableX86;
            executable = Path.Combine(AssemblyDirectory, executable);

            using (var p = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = string.Join(" ", restInstruction),
                    WorkingDirectory = AssemblyDirectory,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                },
            })
            {
                p.Start();
                p.WaitForExit();
                retVal = p.ExitCode == 0;
                p.Close();
            }

            return Task.FromResult(retVal);
        }

        private static async Task<bool> CreateAndRunPip(
            PipProgram program,
            string tempDirectory,
            string outFile,
            IEnumerable<string> restInstructions,
            bool is64Bit)
        {
            Contract.Requires(restInstructions != null);
            Contract.Requires(tempDirectory != null);
            Contract.Requires(!string.IsNullOrEmpty(outFile));

            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();

            using (var fileAccessListener = new FileAccessListener(Events.Log))
            {
                fileAccessListener.RegisterEventSource(BuildXL.Processes.ETWLogger.Log);

                var fileContentTable = FileContentTable.CreateNew();
                var config = ConfigurationHelpers.GetDefaultForTesting(context.PathTable, AbsolutePath.Create(context.PathTable, Path.Combine(tempDirectory, "config.dc")));
                config.Sandbox.LogObservedFileAccesses = true;

                Pip pip = null;

                var instructions = restInstructions as string[] ?? restInstructions.ToArray();

                switch (program)
                {
                    case PipProgram.Cmd:
                        pip = CreateCmdPip(context, tempDirectory, outFile, is64Bit);
                        break;
                    case PipProgram.Self:
                        pip = CreateSelfPip(context, tempDirectory, outFile, instructions, is64Bit);
                        break;
                }

                Contract.Assume(pip != null);
                PipResult executeResult = await Execute(context, fileContentTable, config, pip);

                bool valid = false;

                switch (program)
                {
                    case PipProgram.Cmd:
                        valid = ValidateCmd(fileAccessListener.FileAccesses, outFile, is64Bit);
                        break;
                    case PipProgram.Self:
                        valid = ValidateSelf(
                            fileAccessListener.FileAccesses,
                            instructions.Length > 0 ? instructions[0] : string.Empty,
                            outFile,
                            is64Bit);
                        break;
                }

                return executeResult.Status == PipResultStatus.Succeeded && valid;
            }
        }

        private static async Task<PipResult> Execute(BuildXLContext context, FileContentTable fileContentTable, IConfiguration config, Pip pip)
        {
            Contract.Requires(context != null);
            Contract.Requires(fileContentTable != null);
            Contract.Requires(config != null);
            Contract.Requires(pip != null);

            var loggingContext = BuildXLTestBase.CreateLoggingContextForTest();
            var operationTracker = new OperationTracker(loggingContext);

            using (var env = new Test.BuildXL.Scheduler.Utils.DummyPipExecutionEnvironment(loggingContext, context, config, fileContentTable))
            using (var operationContext = operationTracker.StartOperation(PipExecutorCounter.PipRunningStateDuration, pip.PipId, pip.PipType, env.LoggingContext))
            {
                return await Test.BuildXL.Scheduler.TestPipExecutor.ExecuteAsync(operationContext, env, pip);
            }
        }

        private static Pip CreateCmdPip(BuildXLContext context, string tempDirectory, string outFile, bool is64Bit)
        {
            Contract.Requires(context != null);
            Contract.Requires(tempDirectory != null);
            Contract.Requires(!string.IsNullOrEmpty(outFile));

            var pathTable = context.PathTable;

            string executable = is64Bit ? CmdHelper.CmdX64 : CmdHelper.CmdX86;
            FileArtifact executableArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

            string workingDirectory = AssemblyDirectory;
            AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

            AbsolutePath outFilePath = AbsolutePath.Create(pathTable, outFile);
            FileArtifact outFileArtifact = FileArtifact.CreateSourceFile(outFilePath).CreateNextWrittenVersion();
            var pip = new BuildXL.Pips.Operations.Process(
                executableArtifact,
                workingDirectoryAbsolutePath,
                PipDataBuilder.CreatePipData(
                    context.StringTable,
                    " ",
                    PipDataFragmentEscaping.CRuntimeArgumentRules,
                    "/d",
                    "/c",
                    "echo",
                    "hello",
                    ">",
                    outFileArtifact),
                FileArtifact.Invalid,
                PipData.Invalid,
                ReadOnlyArray<EnvironmentVariable>.Empty,
                FileArtifact.Invalid,
                FileArtifact.Invalid,
                FileArtifact.Invalid,
                AbsolutePath.Create(pathTable, tempDirectory),
                null,
                null,
                ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableArtifact),
                ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(outFileArtifact.WithAttributes()),
                ReadOnlyArray<DirectoryArtifact>.Empty,
                ReadOnlyArray<DirectoryArtifact>.Empty,
                ReadOnlyArray<PipId>.Empty,
                ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(pathTable)),
                ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(pathTable)),
                ReadOnlyArray<StringId>.Empty,
                ReadOnlyArray<int>.Empty,
                ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: PipProvenance.CreateDummy(context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

            return pip;
        }

        private static Pip CreateSelfPip(
            BuildXLContext context,
            string tempDirectory,
            string outFile,
            IEnumerable<string> restInstructions,
            bool is64Bit)
        {
            Contract.Requires(context != null);
            Contract.Requires(tempDirectory != null);
            Contract.Requires(!string.IsNullOrEmpty(outFile));

            var pathTable = context.PathTable;

            string workingDirectory = AssemblyDirectory;
            AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

            AbsolutePath appdataPath = AbsolutePath.Create(pathTable, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            AbsolutePath windowsFolderPath = AbsolutePath.Create(pathTable, Environment.GetFolderPath(Environment.SpecialFolder.Windows));

            string executable = is64Bit ? TestExecutableX64 : TestExecutableX86;
            FileArtifact executableArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, Path.Combine(workingDirectory, executable)));

            AbsolutePath outFilePath = AbsolutePath.Create(pathTable, outFile);
            FileArtifact outFileArtifact = FileArtifact.CreateSourceFile(outFilePath).CreateNextWrittenVersion();
            var pip = new BuildXL.Pips.Operations.Process(
                executableArtifact,
                workingDirectoryAbsolutePath,
                PipDataBuilder.CreatePipData(pathTable.StringTable, " ", PipDataFragmentEscaping.CRuntimeArgumentRules, restInstructions.Select(ri => (PipDataAtom)ri).ToArray()),
                FileArtifact.Invalid,
                PipData.Invalid,
                ReadOnlyArray<EnvironmentVariable>.Empty,
                FileArtifact.Invalid,
                FileArtifact.Invalid,
                FileArtifact.Invalid,
                AbsolutePath.Create(pathTable, tempDirectory),
                null,
                null,
                ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableArtifact),
                ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(outFileArtifact.WithAttributes()),
                ReadOnlyArray<DirectoryArtifact>.Empty,
                ReadOnlyArray<DirectoryArtifact>.Empty,
                ReadOnlyArray<PipId>.Empty,
                ReadOnlyArray<AbsolutePath>.Empty,
                ReadOnlyArray<AbsolutePath>.FromWithoutCopy(
                new[]
                {
                    workingDirectoryAbsolutePath,
                    windowsFolderPath,

                    // For unknown reasons, on some machines the process is accessing LOCALAPPDATA\Microsoft\Windows\Temporary Internet Files\counters.dat
                    appdataPath,
                }),
                ReadOnlyArray<StringId>.Empty,
                ReadOnlyArray<int>.Empty,
                ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: PipProvenance.CreateDummy(context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

            return pip;
        }

        private static bool ValidateCmd(IEnumerable<FileAccessListener.FileAccessDescription> descriptions, string outFile, bool is64Bit)
        {
            Contract.Requires(descriptions != null);
            Contract.Requires(!string.IsNullOrEmpty(outFile));

            // File system redirection causes 32-bit cmd to be called from a 32-bit process.
            string cmd = IntPtr.Size == 4 ? CmdHelper.CmdX86 : (is64Bit ? CmdHelper.CmdX64 : CmdHelper.CmdX86);
            string cmdPrefix = string.Format(CultureInfo.InvariantCulture, "[{0}", cmd);

            bool processCreate = false;
            bool fileCreate = false;

            foreach (var fileAccessDescription in descriptions)
            {
                if (fileAccessDescription.Description.IndexOf("Process(...)", StringComparison.Ordinal) != -1
                    && string.Equals(fileAccessDescription.FilePath, cmd, StringComparison.OrdinalIgnoreCase))
                {
                    processCreate = true;
                }

                if (fileAccessDescription.Description.StartsWith(cmdPrefix, StringComparison.Ordinal)
                    && fileAccessDescription.Description.IndexOf("CreateFile(...", cmdPrefix.Length, StringComparison.Ordinal) != -1
                    && string.Equals(fileAccessDescription.FilePath, outFile, StringComparison.OrdinalIgnoreCase))
                {
                    fileCreate = true;
                }
            }

            return processCreate && fileCreate;
        }

        private static bool ValidateSelf(IEnumerable<FileAccessListener.FileAccessDescription> descriptions, string nextInstruction, string outFile, bool is64Bit)
        {
            Contract.Requires(descriptions != null);
            Contract.Requires(nextInstruction != null);
            Contract.Requires(!string.IsNullOrEmpty(outFile));

            string workingDirectory = AssemblyDirectory;
            string self = is64Bit ? TestExecutableX64 : TestExecutableX86;
            self = Path.Combine(workingDirectory, self);

            string selfPrefix = string.Format(CultureInfo.InvariantCulture, "[{0}", self);
            bool processCreate = false;

            string selfChild = null;

            if (string.Equals(nextInstruction, "selfChild32"))
            {
                selfChild = TestExecutableX86;
            }
            else if (string.Equals(nextInstruction, "selfChild64"))
            {
                selfChild = TestExecutableX64;
            }

            string selfChildPrefix = null;

            if (selfChild != null)
            {
                selfChild = Path.Combine(workingDirectory, selfChild);
                selfChildPrefix = string.Format(CultureInfo.InvariantCulture, "[{0}", selfChild);
            }

            bool fileCreate = selfChild != null;
            bool childCreate = selfChild == null;
            bool childFileCreate = selfChild == null;

            foreach (var fileAccessDescription in descriptions)
            {
                if (fileAccessDescription.Description.IndexOf("Process(...)", StringComparison.Ordinal) != -1
                    && string.Equals(fileAccessDescription.FilePath, self, StringComparison.OrdinalIgnoreCase))
                {
                    processCreate = true;
                }

                if (selfChild == null
                    && fileAccessDescription.Description.StartsWith(selfPrefix, StringComparison.Ordinal)
                    && fileAccessDescription.Description.IndexOf("CreateFile(...", selfPrefix.Length, StringComparison.Ordinal) != -1
                    && string.Equals(fileAccessDescription.FilePath, outFile, StringComparison.OrdinalIgnoreCase))
                {
                    fileCreate = true;
                }

                if (selfChild != null
                    && fileAccessDescription.Description.IndexOf("CreateFile(...", selfPrefix.Length, StringComparison.Ordinal) != -1
                    && string.Equals(fileAccessDescription.FilePath, selfChild, StringComparison.OrdinalIgnoreCase))
                {
                    childCreate = true;
                }

                if (selfChild != null
                    && fileAccessDescription.Description.StartsWith(selfChildPrefix, StringComparison.Ordinal)
                    && fileAccessDescription.Description.IndexOf("CreateFile(...", selfChildPrefix.Length, StringComparison.Ordinal) != -1
                    && string.Equals(fileAccessDescription.FilePath, outFile, StringComparison.OrdinalIgnoreCase))
                {
                    childFileCreate = true;
                }
            }

            return processCreate && fileCreate && childCreate && childFileCreate;
        }

        private static string GetFileName(string directory)
        {
            Contract.Requires(!string.IsNullOrEmpty(directory));
            return Path.Combine(directory, Guid.NewGuid().ToString() + ".txt");
        }

        private static string CreateTempDirectory()
        {
            // string directoryName = string.Format("Test-{0:yyyy-MM-dd_hh-mm-ss-tt}", DateTime.Now);
            // string directory = Path.Combine(Path.GetTempPath(), directoryName);
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }

            Directory.CreateDirectory(directory);
            return directory;
        }

        private static string AssemblyDirectory
        {
            get
            {
                string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                var uri = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(uri.Path);
                return Path.GetDirectoryName(path);
            }
        }

        /// <summary>
        /// Listener for file access.
        /// </summary>
        public sealed class FileAccessListener : BaseEventListener
        {
            private readonly ConcurrentDictionary<FileAccessDescription, object> m_fileAccesses =
                new ConcurrentDictionary<FileAccessDescription, object>();

            public FileAccessListener(Events eventSource)
                : base(eventSource, null)
            {
            }

            public IEnumerable<FileAccessDescription> FileAccesses => m_fileAccesses.Keys.ToArray();

            protected override void OnCritical(EventWrittenEventArgs eventData)
            {
            }

            protected override void OnWarning(EventWrittenEventArgs eventData)
            {
            }

            protected override void OnError(EventWrittenEventArgs eventData)
            {
            }

            protected override void OnInformational(EventWrittenEventArgs eventData)
            {
            }

            protected override void OnVerbose(EventWrittenEventArgs eventData)
            {
                switch (eventData.EventId)
                {
                    case (int)EventId.PipProcessFileAccess:
                        object[] args = eventData.Payload.ToArray();
                        var fileAccess = new FileAccessDescription((string)args[2], (string)args[3]);
                        m_fileAccesses.GetOrAdd(fileAccess, Unit.Void);
                        break;
                }
            }

            protected override void OnAlways(EventWrittenEventArgs eventData)
            {
            }

            /// <summary>
            /// File access description.
            /// </summary>
            public sealed class FileAccessDescription
            {
                /// <summary>
                /// Description.
                /// </summary>
                public readonly string Description;

                /// <summary>
                /// File path.
                /// </summary>
                public readonly string FilePath;

                /// <summary>
                /// Class constructor.
                /// </summary>
                public FileAccessDescription(string description, string filePath)
                {
                    Contract.Requires(description != null);
                    Contract.Requires(filePath != null);

                    Description = description;
                    FilePath = filePath;
                }
            }
        }
    }
}
