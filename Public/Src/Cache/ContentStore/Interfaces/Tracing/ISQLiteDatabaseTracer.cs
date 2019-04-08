// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Interfaces.Tracing
{
    /// <summary>
    ///     Used for SQLite telemetry
    /// </summary>
    public interface ISQLiteDatabaseTracer
    {
        /// <summary>
        ///     Record corruption of the master Sqlite DB
        /// </summary>
        /// <param name="context">Context</param>
        /// <param name="message">message</param>
        void IncrementMasterDBCorruptionCount(Context context, string message);

        /// <summary>
        ///     Record corruption of the backup Sqlite DB
        /// </summary>
        /// <param name="context">Context</param>
        /// <param name="message">message</param>
        void IncrementBackupDBCorruptionCount(Context context, string message);

        /// <summary>
        ///     Record the creation of an in-use marker file for Sqlite DB
        /// </summary>
        /// <param name="context">Context</param>
        /// <param name="message">message</param>
        void IncrementSQLiteMarkerCreatedCount(Context context, string message);

        /// <summary>
        ///     Record the failure to create the in-use marker file for Sqlite DB
        /// </summary>
        /// <param name="context">Context</param>
        /// <param name="exception">Exception</param>
        /// <param name="message">message</param>
        void IncrementSQLiteMarkerCreateFailedCount(Context context, Exception exception, string message);

        /// <summary>
        ///     Record the deletion of an in-use marker file for Sqlite DB
        /// </summary>
        /// <param name="context">Context</param>
        /// <param name="message">message</param>
        void IncrementSQLiteMarkerDeletedCount(Context context, string message);

        /// <summary>
        ///     Record the failure to delete the in-use marker file for Sqlite DB
        /// </summary>
        /// <param name="context">Context</param>
        /// <param name="exception">Exception</param>
        /// <param name="message">message</param>
        void IncrementSQLiteMarkerDeleteFailedCount(Context context, Exception exception, string message);

        /// <summary>
        ///     Record when an in-use marker file has been left behind
        ///     by the previous Sqlite run
        /// </summary>
        /// <param name="context">Context</param>
        /// <param name="message">message</param>
        void IncrementSQLiteMarkerLeftBehindCount(Context context, string message);

        /// <summary>
        ///     Start the integrity check call counter
        /// </summary>
        /// <param name="context">Context</param>
        void SQLiteIntegrityCheckStart(Context context);

        /// <summary>
        ///     Stop the integrity check call counter on success
        /// </summary>
        /// <param name="context">Context</param>
        /// <param name="elapsed">Elapsed Ticks</param>
        void SQLiteIntegrityCheckStopAtSuccess(Context context, TimeSpan elapsed);

        /// <summary>
        ///     Stop the integrity check call counter on failure
        /// </summary>
        /// <param name="context">Context</param>
        /// <param name="exception">Exception</param>
        /// <param name="elapsed">Elapsed Ticks</param>
        void SQLiteIntegrityCheckStopAtFailure(Context context, Exception exception, TimeSpan elapsed);

        /// <summary>
        ///     Record the size of the SQLite database
        /// </summary>
        /// <param name="context">Context</param>
        /// <param name="size">SQLite database size</param>
        void RecordSQLiteDatabaseSize(Context context, long size);

        /// <summary>
        ///     Get all SQLite Database Counters
        /// </summary>
        /// <returns>all SQLite Database Counters</returns>
        CounterSet GetSQLiteDatabaseCounters();
    }
}
