// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.SQLite
{
    /// <summary>
    ///     Controls the journal mode for writes to the database.
    /// </summary>
    /// <remarks>
    ///     Modes are from https://www.sqlite.org/pragma.html#pragma_journal_mode.
    ///     Configurability is enabled for sake of improving execution time of tests
    /// </remarks>
    public enum JournalMode
    {
        /// <summary>
        ///     The DELETE journaling mode is the normal behavior. In the DELETE mode, the rollback journal is deleted at the conclusion of each transaction.
        ///     Indeed, the delete operation is the action that causes the transaction to commit.
        /// </summary>
        DELETE,

        /// <summary>
        ///     The TRUNCATE journaling mode commits transactions by truncating the rollback journal to zero-length instead of deleting it. On many systems,
        ///     truncating a file is much faster than deleting the file since the containing directory does not need to be changed.
        /// </summary>
        TRUNCATE,

        /// <summary>
        ///     The PERSIST journaling mode prevents the rollback journal from being deleted at the end of each transaction. Instead, the header of the journal
        ///     is overwritten with zeros. This will prevent other database connections from rolling the journal back. The PERSIST journaling mode is useful as
        ///     an optimization on platforms where deleting or truncating a file is much more expensive than overwriting the first block of a file with zeros.
        /// </summary>
        PERSIST,

        /// <summary>
        ///     The MEMORY journaling mode stores the rollback journal in volatile RAM. This saves disk I/O but at the expense of database safety and integrity.
        ///     If the application using SQLite crashes in the middle of a transaction when the MEMORY journaling mode is set, then the database file will very
        ///     likely go corrupt.
        /// </summary>
        MEMORY,

        /// <summary>
        ///     The WAL journaling mode uses a write-ahead log instead of a rollback journal to implement transactions. The WAL journaling mode is persistent;
        ///     after being set it stays in effect across multiple database connections and after closing and reopening the database.
        ///     A database in WAL journaling mode can only be accessed by SQLite version 3.7.0 (2010-07-21) or later.
        /// </summary>
        WAL,

        /// <summary>
        ///     The OFF journaling mode disables the rollback journal completely. No rollback journal is ever created and hence there is never a rollback journal
        ///     to delete. The OFF journaling mode disables the atomic commit and rollback capabilities of SQLite. The ROLLBACK command no longer works; it behaves
        ///     in an undefined way. Applications must avoid using the ROLLBACK command when the journal mode is OFF. If the application crashes in the middle
        ///     of a transaction when the OFF journaling mode is set, then the database file will very likely go corrupt.
        /// </summary>
        OFF,
    }
}
