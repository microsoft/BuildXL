// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.PackedTable;

namespace BuildXL.Utilities.PackedExecution
{
    /// <summary>Information about a process pip's execution.</summary>
    /// <remarks>
    /// Based on the BuildXL ProcessPipExecutionPerformance type; note that some fields are not yet supported.
    /// 
    /// Please keep fields sorted alphabetically to ease maintenance.
    /// </remarks>
    public readonly struct ProcessPipExecutionEntry
    {
        /// <summary>I/O counters.</summary>
        public readonly IOCounters IOCounters;

        /// <summary>Kernel-mode execution time. Note that this counter increases as threads in the process tree execute (it is not equivalent to wall clock time).</summary>
        public readonly TimeSpan KernelTime;

        /// <summary>Memory counters.</summary>
        public readonly MemoryCounters MemoryCounters;

        /// <summary>Process count launched by this pip.</summary>
        public readonly uint NumberOfProcesses;

        /// <summary>Time spent executing the entry-point process (possibly zero, such as if this execution was cached).</summary>
        public readonly TimeSpan ProcessExecutionTime;

        /// <summary>Processor used in % (150 means one processor fully used and the other half used)</summary>
        public readonly ushort ProcessorsInPercents;

        /// <summary>Suspended duration in ms</summary>
        public readonly long SuspendedDurationMs;

        /// <summary>User-mode execution time. Note that this counter increases as threads in the process tree execute (it is not equivalent to wall clock time).</summary>
        public readonly TimeSpan UserTime;

        /// <summary>
        /// Construct a PipExecutionEntry.
        /// </summary>
        public ProcessPipExecutionEntry(
            IOCounters ioCounters,
            TimeSpan kernelTime,
            MemoryCounters memoryCounters,
            uint numberOfProcesses,
            TimeSpan processExecutionTime,
            ushort processorsInPercents,
            long suspendedDurationMs,
            TimeSpan userTime)
        {
            IOCounters = ioCounters;
            KernelTime = kernelTime;
            MemoryCounters = memoryCounters;
            NumberOfProcesses = numberOfProcesses;
            ProcessExecutionTime = processExecutionTime;
            ProcessorsInPercents = processorsInPercents;
            SuspendedDurationMs = suspendedDurationMs;
            UserTime = userTime;
        }
    }

    /// <summary>
    /// Table of process pip execution data.
    /// </summary>
    /// <remarks>
    /// This will generally have zero or one PipExecutionEntries per pip; if it has more than one,
    /// it indicates BuildXL produced more than one, which is unusual and which we should discuss with 1ES.
    /// </remarks>
    public class ProcessPipExecutionTable : MultiValueTable<PipId, ProcessPipExecutionEntry>
    {
        /// <summary>
        /// Construct a PipExecutionTable.
        /// </summary>
        public ProcessPipExecutionTable(PipTable pipTable) : base(pipTable)
        {
        }
    }
}
