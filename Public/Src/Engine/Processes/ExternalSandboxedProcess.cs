// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// Class representing external execution of sandboxed process.
    /// </summary>
    public abstract class ExternalSandboxedProcess : ISandboxedProcess
    {
        /// <summary>
        /// Sanboxed process info.
        /// </summary>
        protected SandboxedProcessInfo SandboxedProcessInfo { get; private set; }

        /// <summary>
        /// Creates an instance of <see cref="ExternalSandboxedProcess"/>.
        /// </summary>
        protected ExternalSandboxedProcess(SandboxedProcessInfo sandboxedProcessInfo)
        {
            Contract.Requires(sandboxedProcessInfo != null);

            SandboxedProcessInfo = sandboxedProcessInfo;
        }

        /// <inheritdoc />
        public abstract int ProcessId { get; }

        /// <inheritdoc />
        public abstract void Dispose();

        /// <inheritdoc />
        public abstract string GetAccessedFileName(ReportedFileAccess reportedFileAccess);

        /// <inheritdoc />
        public abstract ulong? GetActivePeakMemoryUsage();

        /// <inheritdoc />
        public abstract long GetDetoursMaxHeapSize();

        /// <inheritdoc />
        public abstract int GetLastMessageCount();

        /// <inheritdoc />
        public abstract Task<SandboxedProcessResult> GetResultAsync();

        /// <inheritdoc />
        public abstract Task KillAsync();

        /// <inheritdoc />
        public abstract void Start();

        /// <summary>
        /// Throws an instance of <see cref="BuildXLException"/>.
        /// </summary>
        protected void ThrowBuildXLException(string message, Exception inner = null)
        {
            throw new BuildXLException($"[Pip{SandboxedProcessInfo.PipSemiStableHash:X16} -- {SandboxedProcessInfo.PipDescription}] {message}", inner);
        }

        /// <summary>
        /// Gets the file to which sandboxed process info will be written.
        /// </summary>
        protected string GetSandboxedProcessInfoFile() => Path.Combine(GetOutputDirectory(), $"SandboxedProcessInfo-Pip{SandboxedProcessInfo.PipSemiStableHash:X16}");

        /// <summary>
        /// Gets the file in which sandboxed process result will be available.
        /// </summary>
        protected string GetSandboxedProcessResultsFile() => Path.Combine(GetOutputDirectory(), $"SandboxedProcessResult-Pip{SandboxedProcessInfo.PipSemiStableHash:X16}");

        /// <summary>
        /// Gets the output directory for starting process externally.
        /// </summary>
        /// <remarks>
        /// This output directory can be the place where BuildXL puts the serialization result of sandboxed process info and
        /// the deserialization result of sandboxed process result. This output directory can also contain the dump of the external process
        /// when it gets killed.
        /// </remarks>
        protected string GetOutputDirectory() => Path.GetDirectoryName(SandboxedProcessInfo.FileStorage.GetFileName(SandboxedProcessFile.StandardOutput));

        /// <summary>
        /// Gets the standard output path for the external executor; not the detoured process.
        /// </summary>
        protected string GetStdOutPath(string hint) => Path.Combine(GetOutputDirectory(), $"{hint ?? string.Empty}-Pip{SandboxedProcessInfo.PipSemiStableHash:X16}.out");

        /// <summary>
        /// Gets the standard error path for the external executor; not the detoured process.
        /// </summary>
        protected string GetStdErrPath(string hint) => Path.Combine(GetOutputDirectory(), $"{hint ?? string.Empty}-Pip{SandboxedProcessInfo.PipSemiStableHash:X16}.err");

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
        /// Serializes sandboxed process info to file.
        /// </summary>
        protected void SerializeSandboxedProcessInfoToFile()
        {
            string file = GetSandboxedProcessInfoFile();
            FileUtilities.CreateDirectory(Path.GetDirectoryName(file));

            try
            {
                using (FileStream stream = File.OpenWrite(file))
                {
                    SandboxedProcessInfo.Serialize(stream);
                }
            }
            catch (IOException ioException)
            {
                ThrowBuildXLException($"Failed to serialize sandboxed process info '{file}'", ioException);
            }
        }

        /// <summary>
        /// Deserializes sandboxed process result from file.
        /// </summary>
        /// <returns></returns>
        protected SandboxedProcessResult DeserializeSandboxedProcessResultFromFile()
        {
            string file = GetSandboxedProcessResultsFile();

            try
            {
                using (FileStream stream = File.OpenRead(file))
                {
                    return SandboxedProcessResult.Deserialize(stream);
                }
            }
            catch (IOException ioException)
            {
                ThrowBuildXLException($"Failed to deserialize sandboxed process result '{file}'", ioException);
            }

            return null;
        }
    }
}