// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.PackedTable;

namespace BuildXL.Utilities.PackedExecution
{
    /// <summary>Indicates the manner in which a pip executed.</summary>
    public enum PipExecutionLevel
    {
        /// <summary>The pip's full work was performed.</summary>
        Executed,

        /// <summary>The pip was cached, and some work was performed to deploy it from cache.</summary>
        Cached,

        /// <summary>The pip was fully up to date.</summary>
        UpToDate,

        /// <summary>The pip failed.</summary>
        Failed,
    }

    /// <summary>Information about a pip's execution.</summary>
    /// <remarks>Based on the BuildXL PipExecutionPerformance type.</remarks>
    public readonly struct PipExecutionEntry
    {
        /// <summary>
        /// Indicates the manner in which a pip executed.
        /// </summary>
        public readonly PipExecutionLevel ExecutionLevel;

        /// <summary>
        /// Start time in UTC.
        /// </summary>
        public readonly DateTime ExecutionStart;

        /// <summary>
        /// Stop time in UTC.
        /// </summary>
        public readonly DateTime ExecutionStop;

        /// <summary>Worker identifier.</summary>
        public readonly WorkerId WorkerId;

        /// <summary>Construct a PipExecutionEntry.
        /// </summary>
        public PipExecutionEntry(
            PipExecutionLevel executionLevel,
            DateTime executionStart,
            DateTime executionStop,
            WorkerId workerId)
        {
            ExecutionLevel = executionLevel;
            ExecutionStart = executionStart;
            ExecutionStop = executionStop;
            WorkerId = workerId;
        }
    }

    /// <summary>Table of pip execution data.</summary>
    /// <remarks>
    /// This will generally have exactly one entry per pip, so it could be a SingleValueTable, but it is more
    /// convenient to construct as a MultiValueTable.
    /// </remarks>
    public class PipExecutionTable : MultiValueTable<PipId, PipExecutionEntry>
    {
        /// <summary>
        /// Construct a PipExecutionTable.
        /// </summary>
        public PipExecutionTable(PipTable pipTable) : base(pipTable)
        {
        }
    }
}
