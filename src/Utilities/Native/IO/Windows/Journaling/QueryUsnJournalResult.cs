// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Native.IO.Windows
{
    /// <summary>
    /// Result of querying a USN journal with <see cref="FileSystemWin.TryQueryUsnJournal"/>.
    /// </summary>
    public sealed class QueryUsnJournalResult
    {
        /// <summary>
        /// Status indication of the query attempt.
        /// </summary>
        public readonly QueryUsnJournalStatus Status;

        private readonly QueryUsnJournalData m_data;

        /// <nodoc />
        public QueryUsnJournalResult(QueryUsnJournalStatus status, QueryUsnJournalData data)
        {
            Contract.Requires((status == QueryUsnJournalStatus.Success) == (data != null), "Journal data should be present only on success");

            Status = status;
            m_data = data;
        }

        /// <summary>
        /// Indicates if querying the journal succeeded.
        /// </summary>
        public bool Succeeded => Status == QueryUsnJournalStatus.Success;

        /// <summary>
        /// Returns the queried data (fails if not <see cref="Succeeded"/>).
        /// </summary>
        public QueryUsnJournalData Data
        {
            get
            {
                Contract.Requires(Succeeded);
                return m_data;
            }
        }
    }
}
