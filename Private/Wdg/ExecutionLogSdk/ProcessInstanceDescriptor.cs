// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Pips;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// Describes a single process that ran as part of the build.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix", Justification = "This class should not be called a Collection")]
    public sealed class ProcessInstanceDescriptor
    {
        #region Public properties

        /// <summary>
        /// The process Id of the process.
        /// </summary>
        public uint ProcessId { get; set; }

        /// <summary>
        /// The process executable.
        /// </summary>
        public string ProcessExecutable { get; set; }

        /// <summary>
        /// The command line arguments of the process (it also includes the process executable as argument 0).
        /// </summary>
        public string ProcessArgs { get; set; }

        /// <summary>
        /// Represents the amount of time this process spent in kernel mode code.
        /// </summary>
        public TimeSpan KernelTime { get; set; }

        /// <summary>
        /// Represents the amount of time this process spent in user mode code.
        /// </summary>
        public TimeSpan UserTime { get; set; }

        /// <summary>
        /// Represents the input and output associated with this process.
        /// </summary>
        public IOCounters IO { get; set; }

        /// <summary>
        /// Represents the creation time (in UTC) of the process.
        /// </summary>
        public DateTime CreationTime { get; set; }

        /// <summary>
        /// Represents the exit time (in UTC) of the process.
        /// </summary>
        public DateTime ExitTime { get; set; }

        /// <summary>
        /// The process exit code. 0xBAAAAAAD means DllProcessDetach was not called on DetoursServices.dll, so the value is not initialized.
        /// </summary>
        public uint ExitCode { get; set; }

        /// <summary>
        /// The process Id of the current process's parent.
        /// </summary>
        public uint ParentProcessId { get; set; }
        #endregion

        #region Internal properties

        /// <summary>
        /// Internal constructor
        /// </summary>
        /// <param name="processId">The process id of the process</param>
        /// <param name="processExecutable">The name of the executable that runs the process</param>
        /// <param name="processArgs">Process arguments</param>
        /// <param name="kernelTime">The amount of time the process spent executing kernel mode code</param>
        /// <param name="userTime">The amount of time the process spent executing user mode code</param>
        /// <param name="ioCounters">Input Output counters and byte transfer count for read, write and other</param>
        /// <param name="creationTime">The time (in UTC) that the process was created</param>
        /// <param name="exitTime">The time (in UTC) that the process exited</param>
        internal ProcessInstanceDescriptor(
            uint processId,
            string processExecutable,
            string processArgs,
            TimeSpan kernelTime,
            TimeSpan userTime,
            IOCounters ioCounters,
            DateTime creationTime,
            DateTime exitTime,
            uint exitCode,
            uint parentProcessId)
        {
            ProcessId = processId;
            ProcessExecutable = processExecutable;
            ProcessArgs = processArgs;
            KernelTime = kernelTime;
            UserTime = userTime;
            IO = ioCounters;
            CreationTime = creationTime;
            ExitTime = exitTime;
            ExitCode = exitCode;
            ParentProcessId = parentProcessId;
        }
        #endregion

        public override string ToString()
        {
            return ProcessExecutable;
        }
    }
}
