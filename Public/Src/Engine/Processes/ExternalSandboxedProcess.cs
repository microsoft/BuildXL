// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
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
        /// <returns></returns>
        protected string GetSandboxedProcessInfoFile()
        {
            string directory = Path.GetDirectoryName(SandboxedProcessInfo.FileStorage.GetFileName(SandboxedProcessFile.StandardOutput));
            string file = Path.Combine(directory, $"SandboxedProcessInfo-Pip{SandboxedProcessInfo.PipSemiStableHash:X16}");

            return file;
        }

        /// <summary>
        /// Gets the file to which sandboxed process result will be available.
        /// </summary>
        /// <returns></returns>
        protected string GetSandboxedProcessResultsFile()
        {
            string directory = Path.GetDirectoryName(SandboxedProcessInfo.FileStorage.GetFileName(SandboxedProcessFile.StandardOutput));
            string file = Path.Combine(directory, $"SandboxedProcessResult-Pip{SandboxedProcessInfo.PipSemiStableHash:X16}");

            return file;
        }

        /// <summary>
        /// Serializes sandboxed process info to file.
        /// </summary>
        protected void SerializeSandboxedProcessInfoToFile()
        {
            string file = GetSandboxedProcessInfoFile();

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
