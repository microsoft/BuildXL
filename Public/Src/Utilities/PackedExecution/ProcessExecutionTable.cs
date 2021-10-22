// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.PackedTable;

namespace BuildXL.Utilities.PackedExecution
{
    /// <summary>Information about a process's execution.</summary>
    /// <remarks>Based on the BuildXL ReportedProcess class; please keep sorted alphabetically.</remarks>
    public readonly struct ProcessExecutionEntry
    {
        /// <summary>The time this reported process object was created.</summary>
        public readonly DateTime CreationTime;

        /// <summary>The process exit code. 0xBAAAAAAD means DllProcessDetach was not called on DetoursServices.dll, so the value is not initialized.</summary>
        public readonly uint ExitCode;

        /// <summary>The time this reported process object exited.</summary>
        public readonly DateTime ExitTime;

        /// <summary>The IO this process is responsible for.</summary>
        public readonly IOCounters IOCounters;

        /// <summary>The amount of time the process spent in kernel mode code.</summary>
        public readonly TimeSpan KernelTime;

        /// <summary>The process Id of the current process's parent.</summary>
        public readonly uint ParentProcessId;

        /// <summary>The path of the executable file of the process.</summary>
        public readonly NameId Path;

        /// <summary>The (not necessarily unique) process id</summary>
        public readonly uint ProcessId;

        /* TODO: not capturing process args yet for fear they will blow up in storage. Look at a better encoding?
        /// <summary>The command line arguments of the process</summary>
        public readonly string ProcessArgs;
        */

        /// <summary>The amount of time the process spent in user mode code.</summary>
        public readonly TimeSpan UserTime;

        /// <summary>Constructor.</summary>
        public ProcessExecutionEntry(
            DateTime creationTime,
            uint exitCode,
            DateTime exitTime,
            IOCounters ioCounters,
            TimeSpan kernelTime,
            uint parentProcessId,
            NameId path,
            uint processId,
            TimeSpan userTime)
        {
            CreationTime = creationTime;
            ExitCode = exitCode;
            ExitTime = exitTime;
            IOCounters = ioCounters;
            KernelTime = kernelTime;
            ParentProcessId = parentProcessId;
            Path = path;
            ProcessId = processId;
            UserTime = userTime;
        }
    }

    /// <summary>Table of process execution data.</summary>
    /// <remarks>Since most pips in the overall graph are not process pips, most entries in this table will be empty.</remarks>
    public class ProcessExecutionTable : MultiValueTable<PipId, ProcessExecutionEntry>
    {
        /// <summary>
        /// Construct a ProcessExecutionTable.
        /// </summary>
        public ProcessExecutionTable(PipTable pipTable) : base(pipTable)
        {
        }
    }
}
