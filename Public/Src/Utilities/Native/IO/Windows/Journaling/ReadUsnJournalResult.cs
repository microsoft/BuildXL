// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Native.IO.Windows
{
    /// <summary>
    /// Result of reading a USN journal with <see cref="FileSystemWin.TryReadUsnJournal"/>.
    /// </summary>
    public sealed class ReadUsnJournalResult
    {
        /// <summary>
        /// Status indication of the read attempt.
        /// </summary>
        public readonly ReadUsnJournalStatus Status;

        /// <summary>
        /// If the read <see cref="Succeeded"/>, specifies the next USN that will be recorded in the journal
        /// (a continuation cursor for futher reads).
        /// </summary>
        public readonly Usn NextUsn;

        /// <summary>
        /// If the read <see cref="Succeeded"/>, the list of records retrieved.
        /// </summary>
        public readonly IReadOnlyCollection<UsnRecord> Records;

        /// <nodoc />
        public ReadUsnJournalResult(ReadUsnJournalStatus status, Usn nextUsn, IReadOnlyCollection<UsnRecord> records)
        {
            Contract.Requires((status == ReadUsnJournalStatus.Success) == (records != null), "Records list should be present only on success");

            Status = status;
            NextUsn = nextUsn;
            Records = records;
        }

        /// <summary>
        /// Indicates if reading the journal succeeded.
        /// </summary>
        public bool Succeeded => Status == ReadUsnJournalStatus.Success;
    }
}
