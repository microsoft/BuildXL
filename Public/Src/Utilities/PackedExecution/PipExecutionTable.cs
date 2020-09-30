// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.PackedTable;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.PackedExecution
{
    /// <summary>
    /// Information about a process pip's execution.
    /// </summary>
    /// <remarks>
    /// Right now this is the most rudimentary information imaginable.
    /// </remarks>
    public struct PipExecutionEntry
    {
        /// <summary>
        /// The worker on which this pip executed.
        /// </summary>
        public readonly WorkerId Worker;

        /// <summary>
        /// Construct a PipExecutionEntry.
        /// </summary>
        public PipExecutionEntry(
            WorkerId worker)
        {
            Worker = worker;
        }

        /// <summary>
        /// Has this execution entry been initialized?
        /// </summary>
        /// <returns></returns>
        public bool IsInitialized()
        {
            return Worker.FromId() > 0;
        }
    }

    /// <summary>
    /// Table of pip execution data.
    /// </summary>
    /// <remarks>
    /// Since this table has the master PipTable as its base table, this table will have as many entries as
    /// the PipTable. Since most pips in the overall graph are not process pips, that means most entries in
    /// this table will be empty (e.g. 
    /// </remarks>
    public class PipExecutionTable : SingleValueTable<PipId, PipExecutionEntry>
    {
        /// <summary>
        /// Construct a PipExecutionTable.
        /// </summary>
        public PipExecutionTable(PipTable pipTable) : base(pipTable)
        {
        }
    }
}
