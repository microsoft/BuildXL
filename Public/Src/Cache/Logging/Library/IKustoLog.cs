// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.Stores;

#nullable enable

namespace BuildXL.Cache.Logging
{
    /// <summary>
    ///     Allows adding log strings to a Kusto table.
    /// </summary>
    /// <remarks>
    ///     The log strings passed for writing must be already formatted to match the ingestion format configured for the targed Kusto table.
    ///     All supported ingestion formats are described in https://kusto.azurewebsites.net/docs/ingestion-supported-formats.html.
    ///     The interface is derived from <see cref="IStartupShutdownSlim" /> to provide a way for the log implementation to react on startup/shutdown events
    ///     for example to recover logs created during a previous run or to flush to storage any buffered in memory events.
    /// </remarks>
    public interface IKustoLog : IStartupShutdownSlim
    {
        /// <summary>
        ///     Write a single log line
        /// </summary>
        void Write(string log);

        /// <summary>
        ///     Write multiple log lines
        /// </summary>
        void WriteBatch(IEnumerable<string> logs);
    }
}