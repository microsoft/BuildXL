// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Native.IO;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// A (nested) process instance reported via Detours
    /// </summary>
    /// <remarks>
    /// An instance of this class uniquely identifies a particular instance of a process
    /// </remarks>
    public sealed class ReportedProcess
    {
        /// <summary>
        /// The path of the executable file of the process.
        /// </summary>
        public readonly string Path;

        /// <summary>
        /// The (not necessarily unique) process id
        /// </summary>
        public readonly uint ProcessId;

        /// <summary>
        /// The command line arguments of  the process
        /// </summary>
        public readonly string ProcessArgs;

        /// <summary>
        /// The IO this process is responsible for.
        /// </summary>
        public IOCounters IOCounters;

        /// <summary>
        /// The time this reported process object was created.
        /// </summary>
        public DateTime CreationTime = DateTime.UtcNow;

        /// <summary>
        /// The time this reported process object was created.
        /// </summary>
        public DateTime ExitTime = DateTime.UtcNow;

        /// <summary>
        /// Represents the amount of time the process spent in kernel mode code.
        /// </summary>
        public TimeSpan KernelTime = TimeSpan.Zero;

        /// <summary>
        /// Represent the amount of time the process spent in user mode code.
        /// </summary>
        public TimeSpan UserTime = TimeSpan.Zero;

        /// <summary>
        /// The process exit code. 0xBAAAAAAD means DllProcessDetach was not called on DetoursServices.dll, so the value is not initialized.
        /// </summary>
        public uint ExitCode;

        /// <summary>
        /// The process Id of the current process's parent.
        /// </summary>
        public uint ParentProcessId;

        /// <summary>
        /// Creates an instance of this class.
        /// </summary>
        /// <param name="processId">The process ID of the reported process.</param>
        /// <param name="path">The full path and file name of the reported process.</param>
        /// <param name="args">The command line arguments of the reported process.</param>
        public ReportedProcess(uint processId, string path, string args)
        {
            Contract.Requires(path != null);
            ProcessId = processId;
            ProcessArgs = args;
            Path = path;
            ExitCode = ExitCodes.UninitializedProcessExitCode;
        }

        /// <summary>
        /// Creates an instance of this class.
        /// </summary>
        /// <param name="processId">The process ID of the reported process.</param>
        /// <param name="path">The full path and file name of the reported process.</param>
        public ReportedProcess(uint processId, string path)
            : this(processId, path, string.Empty)
        {
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(ProcessId);
            writer.Write(Path);
            writer.Write(ProcessArgs);
            writer.Write(CreationTime);
            writer.Write(ExitTime);
            writer.Write(KernelTime);
            writer.Write(UserTime);
            IOCounters.Serialize(writer);
            writer.Write(ExitCode);
            writer.Write(ParentProcessId);
        }

        /// <nodoc />
        public static ReportedProcess Deserialize(BuildXLReader reader)
        {
            var reportedProcess = new ReportedProcess(reader.ReadUInt32(), reader.ReadString(), reader.ReadString());
            reportedProcess.CreationTime = reader.ReadDateTime();
            reportedProcess.ExitTime = reader.ReadDateTime();
            reportedProcess.KernelTime = reader.ReadTimeSpan();
            reportedProcess.UserTime = reader.ReadTimeSpan();
            reportedProcess.IOCounters = IOCounters.Deserialize(reader);
            reportedProcess.ExitCode = reader.ReadUInt32();
            reportedProcess.ParentProcessId = reader.ReadUInt32();

            return reportedProcess;
        }
    }
}
