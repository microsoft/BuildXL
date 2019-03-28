// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.SQLite
{
    /// <summary>
    ///     Controls the synchronization mode for writes to the database.
    /// </summary>
    /// <remarks>
    ///     Modes numbers and descriptions are from https://www.sqlite.org/pragma.html#pragma_synchronous.
    ///     Note that they are not identical to <see cref="System.Data.SQLite.SynchronizationModes" /> or else we
    ///     may have re-used that.
    /// </remarks>
    public enum SynchronizationMode
    {
        /// <summary>
        ///     With synchronous OFF (0), SQLite continues without syncing as soon as it has handed data off to the operating system.
        ///     If the application running SQLite crashes, the data will be safe, but the database might become corrupted if the
        ///     operating system crashes or the computer loses power before that data has been written to the disk surface.
        ///     On the other hand, commits can be orders of magnitude faster with synchronous OFF.
        /// </summary>
        Off = 0,

        /// <summary>
        ///     When synchronous is NORMAL (1), the SQLite database engine will still sync at the most critical moments,
        ///     but less often than in FULL mode. There is a very small (though non-zero) chance that a power failure at just the
        ///     wrong time could corrupt the database in NORMAL mode. But in practice, you are more likely to suffer a catastrophic
        ///     disk failure or some other unrecoverable hardware fault. Many applications choose NORMAL when in WAL mode.
        /// </summary>
        Normal = 1,

        /// <summary>
        ///     When synchronous is FULL (2), the SQLite database engine will use the xSync method of the VFS to ensure that all
        ///     content is safely written to the disk surface prior to continuing. This ensures that an operating system crash or power
        ///     failure will not corrupt the database. FULL synchronous is very safe, but it is also slower. FULL is the most commonly
        ///     used synchronous setting when not in WAL mode.
        /// </summary>
        Full = 2,

        /// <summary>
        ///     EXTRA synchronous is like FULL with the addition that the directory containing a rollback journal is synced after
        ///     that journal is unlinked to commit a transaction in DELETE mode. EXTRA provides additional durability if the commit
        ///     is followed closely by a power loss.
        /// </summary>
        Extra = 3
    }
}
