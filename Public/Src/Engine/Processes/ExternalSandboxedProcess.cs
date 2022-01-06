// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Interop;
using BuildXL.Native.IO;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// Class representing external execution of sandboxed process.
    /// </summary>
    public abstract class ExternalSandboxedProcess : ISandboxedProcess
    {
        private const string SandboxedProcessInfoFileName = "SandboxedProcessInfo";
        private const string SandboxedProcessExecutorTestHookFileName = "SandboxedProcessExecutorTestHook";
        private const string SandboxedProcessResultFileName = "SandboxedProcessResult";
        private const string StdOutFileName = "std.out";
        private const string StdErrFileName = "std.err";

        /// <summary>
        /// Empty file access set.
        /// </summary>
        protected static readonly ISet<ReportedFileAccess> EmptyFileAccessesSet = new HashSet<ReportedFileAccess>();

        /// <summary>
        /// Sandboxed process info.
        /// </summary>
        public SandboxedProcessInfo SandboxedProcessInfo { get; private set; }

        /// <summary>
        /// SandboxedProcessExecutor Test Hook.
        /// </summary>
        protected SandboxedProcessExecutorTestHook SandboxedProcessExecutorTestHook { get; private set; }

        /// <summary>
        /// Working directory.
        /// </summary>
        /// <remarks>
        /// This working directory can be the place where BuildXL puts the serialization result of sandboxed process info and
        /// the deserialization result of sandboxed process result. This working directory can also contain the dump of the external process
        /// when it gets killed.
        /// </remarks>
        protected string WorkingDirectory { get; private set; }

        /// <summary>
        /// Dump exception.
        /// </summary>
        protected Exception DumpCreationException;

        /// <summary>
        /// Creates an instance of <see cref="ExternalSandboxedProcess"/>.
        /// </summary>
        protected ExternalSandboxedProcess(
            SandboxedProcessInfo sandboxedProcessInfo,
            string workingDirectoryRoot,
            SandboxedProcessExecutorTestHook sandboxedProcessExecutorTestHook = null)
        {
            Contract.Requires(sandboxedProcessInfo != null);
            Contract.Requires(!string.IsNullOrEmpty(workingDirectoryRoot));

            SandboxedProcessInfo = sandboxedProcessInfo;
            SandboxedProcessExecutorTestHook = sandboxedProcessExecutorTestHook;
            WorkingDirectory = Path.Combine(workingDirectoryRoot, $"Pip{SandboxedProcessInfo.PipSemiStableHash:X16}");
        }

        /// <inheritdoc />
        public abstract int ProcessId { get; }

        /// <inheritdoc />
        public abstract void Dispose();

        /// <inheritdoc />
        public virtual string GetAccessedFileName(ReportedFileAccess reportedFileAccess) => null;

        /// <inheritdoc />
        public abstract ProcessMemoryCountersSnapshot? GetMemoryCountersSnapshot();

        /// <inheritdoc />
        public abstract EmptyWorkingSetResult TryEmptyWorkingSet(bool isSuspend);

        /// <inheritdoc />
        public abstract bool TryResumeProcess();

        /// <inheritdoc />
        public virtual long GetDetoursMaxHeapSize() => 0;

        /// <inheritdoc />
        public virtual int GetLastMessageCount() => 0;

        /// <inheritdoc />
        public abstract Task<SandboxedProcessResult> GetResultAsync();

        /// <inheritdoc />
        public abstract Task KillAsync();

        /// <inheritdoc />
        public virtual void Start() => EnsureEmptyWorkingDirectory();

        /// <summary>
        /// Ensures that working directory exists and is empty.
        /// </summary>
        protected void EnsureEmptyWorkingDirectory()
        {
            if (FileUtilities.DirectoryExistsNoFollow(WorkingDirectory))
            {
                FileUtilities.DeleteDirectoryContents(WorkingDirectory);
            }
            else
            {
                FileUtilities.CreateDirectory(WorkingDirectory);
            }
        }

        /// <summary>
        /// Some sandboxed processes may want to clean up the whole working directory to prevent it from becoming large.
        /// </summary>
        /// <remarks>
        /// Note that this method does not clean up the working directory of the underlying process. It cleans up the
        /// working directory of the external sandboxed process executor that is defined in the constructor of <see cref="ExternalSandboxedProcess"/>.
        /// The reason for this is to keep the entries of the root of working directories small. Particularly when the process needs
        /// to execute remotely, the directory entries of the root need to be sent to the remote agent. The more entries, the bigger the data
        /// needs to be sent remotely.
        /// </remarks>
        protected void CleanUpWorkingDirectory()
        {
            FileUtilities.DeleteDirectoryContents(WorkingDirectory, deleteRootDirectory: true);
        }

        /// <summary>
        /// Throws an instance of <see cref="BuildXLException"/>.
        /// </summary>
        protected void ThrowBuildXLException(string message, Exception inner = null)
        {
            throw new BuildXLException($"{SandboxedProcessInfo.Provenance} {message}", inner);
        }

        /// <summary>
        /// Gets the file to which sandboxed process info will be written.
        /// </summary>
        protected string SandboxedProcessInfoFile => Path.Combine(WorkingDirectory, SandboxedProcessInfoFileName);

        /// <summary>
        /// Gets the file to which SandboxedProcessExecutorTestHook will be written.
        /// </summary>
        protected string SandboxedProcessExecutorTestHookFile => Path.Combine(WorkingDirectory, SandboxedProcessExecutorTestHookFileName);

        /// <summary>
        /// Gets the file in which sandboxed process result will be available.
        /// </summary>
        protected string SandboxedProcessResultsFile => Path.Combine(WorkingDirectory, SandboxedProcessResultFileName);

        /// <summary>
        /// Gets the standard output path for the external executor; not the detoured process.
        /// </summary>
        protected string GetStdOutPath(string hint) => Path.Combine(WorkingDirectory, $"{PrefixWithHintIfAny(hint)}{StdOutFileName}");

        /// <summary>
        /// Gets the standard error path for the external executor; not the detoured process.
        /// </summary>
        protected string GetStdErrPath(string hint) => Path.Combine(WorkingDirectory, $"{PrefixWithHintIfAny(hint)}{StdErrFileName}");

        private static string PrefixWithHintIfAny(string hint) => hint != null ? hint + "-" : string.Empty;

        /// <summary>
        /// Standard output for the external executor.
        /// </summary>
        public abstract string StdOut { get; }

        /// <summary>
        /// Standard error for the external executor.
        /// </summary>
        public abstract string StdErr { get; }

        /// <summary>
        /// Gets the exit code of the external executor.
        /// </summary>
        public abstract int? ExitCode { get; }

        /// <summary>
        /// Serializes sandboxed process input files.
        /// </summary>
        protected void SerializeSandboxedProcessInputFile(string path, Action<FileStream> serialize)
        {
            FileUtilities.CreateDirectory(Path.GetDirectoryName(path));

            try
            {
                using (FileStream stream = File.OpenWrite(path))
                {
                    serialize(stream);
                }
            }
            catch (IOException ioException)
            {
                ThrowBuildXLException($"Failed to serialize sandboxed process input '{path}'", ioException);
            }
        }

        /// <summary>
        /// Deserializes sandboxed process result from file.
        /// </summary>
        protected SandboxedProcessResult DeserializeSandboxedProcessResultFromFile()
        {
            string file = SandboxedProcessResultsFile;

            Func<BuildXLReader, AbsolutePath> readPath = reader =>
            {
                bool isAbsolutePath = reader.ReadBoolean();
                if (isAbsolutePath)
                {
                    return reader.ReadAbsolutePath();
                }
                else
                {
                    string path = reader.ReadString();
                    return AbsolutePath.Create(SandboxedProcessInfo.PathTable, path);
                }
            };

            try
            {
                using (FileStream stream = File.OpenRead(file))
                {
                    return SandboxedProcessResult.Deserialize(stream, readPath);
                }
            }
            catch (IOException ioException)
            {
                ThrowBuildXLException($"Failed to deserialize sandboxed process result '{file}'", ioException);
                return null;
            }
        }

        /// <summary>
        /// Kills process executor.
        /// </summary>
        protected Task KillProcessExecutorAsync(AsyncProcessExecutor executor)
        {
            Contract.Requires(executor != null);

            ProcessDumper.TryDumpProcessAndChildren(ProcessId, WorkingDirectory, out DumpCreationException);

            return executor.KillAsync();
        }

        /// <summary>
        /// Create generic result for failure.
        /// </summary>
        protected SandboxedProcessResult CreateResultForFailure(
            int exitCode,
            bool killed,
            bool timedOut,
            string output, 
            string error,
            string hint)
        {
            var standardFiles = new SandboxedProcessStandardFiles(GetStdOutPath(hint), GetStdErrPath(hint));
            var storage = new StandardFileStorage(standardFiles);

            return new SandboxedProcessResult
            {
                ExitCode = exitCode,
                Killed = killed,
                TimedOut = timedOut,
                HasDetoursInjectionFailures = false,
                StandardOutput = new SandboxedProcessOutput(output.Length, output, null, Console.OutputEncoding, storage, SandboxedProcessFile.StandardOutput, null),
                StandardError = new SandboxedProcessOutput(error.Length, error, null, Console.OutputEncoding, storage, SandboxedProcessFile.StandardError, null),
                HasReadWriteToReadFileAccessRequest = false,
                AllUnexpectedFileAccesses = EmptyFileAccessesSet,
                FileAccesses = EmptyFileAccessesSet,
                DetouringStatuses = new ProcessDetouringStatusData[0],
                ExplicitlyReportedFileAccesses = EmptyFileAccessesSet,
                Processes = new ReportedProcess[0],
                MessageProcessingFailure = null,
                DumpCreationException = DumpCreationException,
                DumpFileDirectory = WorkingDirectory,
                PrimaryProcessTimes = new ProcessTimes(0, 0, 0, 0),
                SurvivingChildProcesses = new ReportedProcess[0],
            };
        }

        /// <summary>
        /// Appends line to string builder if line is not null.
        /// </summary>
        protected static void AppendLineIfNotNull(StringBuilder sb, string line)
        {
            if (line != null)
            {
                sb.AppendLine(line);
            }
        }

        /// <summary>
        /// Starts process asynchronously.
        /// </summary>
        public static Task<ISandboxedProcess> StartAsync(
            SandboxedProcessInfo info, 
            Func<SandboxedProcessInfo, ExternalSandboxedProcess> externalSandboxedProcessFactory)
        {
            return Task.Factory.StartNew(() =>
            {
                ISandboxedProcess process = externalSandboxedProcessFactory(info);

                try
                {
                    process.Start();
                }
                catch
                {
                    process?.Dispose();
                    throw;
                }

                return process;
            });
        }

        /// <summary>
        /// Logs external execution.
        /// </summary>
        protected void LogExternalExecution(string message)
        {
            if (SandboxedProcessInfo.LoggingContext != null)
            {
                Tracing.Logger.Log.PipProcessExternalExecution(
                    SandboxedProcessInfo.LoggingContext, 
                    SandboxedProcessInfo.PipSemiStableHash, 
                    SandboxedProcessInfo.PipDescription, message);
            }
        }
    }
}