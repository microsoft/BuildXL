// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Native.IO;
using BuildXL.Utilities.Core;

namespace BuildXL.Processes
{
    /// <summary>
    /// A (nested) process instance reported via Detours
    /// </summary>
    /// <remarks>
    /// An instance of this class uniquely identifies a particular instance of a process
    /// </remarks>
    public sealed class ReportedProcess : IEquatable<ReportedProcess>
    {
        /// <summary>
        /// The path of the executable file of the process.
        /// </summary>
        public string Path { get; private set; }

        /// <summary>
        /// The (not necessarily unique) process id
        /// </summary>
        public readonly uint ProcessId;

        /// <summary>
        /// The command line arguments of  the process
        /// </summary>
        public string ProcessArgs { get; private set; }

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
        public ReportedProcess(uint processId, string path)
            : this(processId, path, string.Empty)
        {
        }

        /// <summary>
        /// Creates an instance of this class.
        /// </summary>
        /// <param name="processId">The process ID of the reported process.</param>
        /// <param name="path">The full path and file name of the reported process.</param>
        /// <param name="args">The command line arguments of the reported process.</param>
        public ReportedProcess(uint processId, string path, string args) 
            : this(processId, 0, path, args) 
        {
        }

        /// <summary>
        /// Creates an instance of this class.
        /// </summary>
        /// <param name="processId">The process ID of the reported process.</param>
        /// <param name="parentProcessId">The parent process ID of the reported process</param>
        /// <param name="path">The full path and file name of the reported process.</param>
        /// <param name="args">The command line arguments of the reported process.</param>
        public ReportedProcess(uint processId, uint parentProcessId, string path, string args)
        {
            Contract.Requires(path != null);
            ProcessId = processId;
            ParentProcessId = parentProcessId;
            ProcessArgs = args;
            Path = path;
            ExitCode = ExitCodes.UninitializedProcessExitCode;
        }

        /// <summary>
        /// When an exec call occurs on a Process, its process image and process args must be updated.
        /// </summary>
        /// <remarks>
        /// Only applicable on Linux platforms.
        /// </remarks>
        public void UpdateOnPathAndArgsOnExec(string path, string args)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));
            // Process args could potentially be empty if the reported process was called without any args, so we won't do an empty check here for that
            Contract.Requires(args != null);
            Path = path;
            ProcessArgs = args;
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
            var reportedProcess = new ReportedProcess(
                processId: reader.ReadUInt32(), 
                path:      reader.ReadString(), 
                args:      reader.ReadString())
            {
                CreationTime    = reader.ReadDateTime(),
                ExitTime        = reader.ReadDateTime(),
                KernelTime      = reader.ReadTimeSpan(),
                UserTime        = reader.ReadTimeSpan(),
                IOCounters      = IOCounters.Deserialize(reader),
                ExitCode        = reader.ReadUInt32(),
                ParentProcessId = reader.ReadUInt32()
            };

            return reportedProcess;
        }

        /// <nodoc />
        public bool Equals(ReportedProcess other)
        {
            return other != null &&
                ProcessId == other.ProcessId &&
                Path == other.Path &&
                ProcessArgs == other.ProcessArgs &&
                CreationTime == other.CreationTime &&
                ExitTime == other.ExitTime &&
                KernelTime == other.KernelTime &&
                UserTime == other.UserTime &&
                IOCounters == other.IOCounters &&
                ExitCode == other.ExitCode &&
                ParentProcessId == other.ParentProcessId;
        }

        /// <nodoc />
        public override int GetHashCode() => (int) ProcessId;
                
        /// <nodoc />
        public override bool Equals(object obj) => obj as ReportedProcess is var reportedProcess && reportedProcess != null && Equals(reportedProcess);
    }
}