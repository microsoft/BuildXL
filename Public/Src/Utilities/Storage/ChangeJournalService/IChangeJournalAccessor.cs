// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Native.IO.Windows;
using BuildXL.Storage.ChangeJournalService.Protocol;

namespace BuildXL.Storage.ChangeJournalService
{
    /// <summary>
    /// Represents access to change journals that can expose individual change records in addition to overall journal metadata.
    /// </summary>
    public interface IChangeJournalAccessor
    {
        /// <summary>
        /// Attempts to read the journal based on the <paramref name="request" /> parameters. Before completion, one or more
        /// records
        /// may be pushed via <paramref name="onUsnRecordReceived" />.
        /// </summary>
        MaybeResponse<ReadJournalResponse> ReadJournal(ReadJournalRequest request, Action<UsnRecord> onUsnRecordReceived);

        /// <summary>
        /// Queries the specified journal for journal-level metadata.
        /// </summary>
        MaybeResponse<QueryUsnJournalResult> QueryJournal(QueryJournalRequest request);
    }
}
