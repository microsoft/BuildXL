// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.SQLite
{
    /// <summary>
    ///     Base configuration for any <see cref="SQLiteDatabase{TTracer}"/>
    /// </summary>
    public class SQLiteDatabaseConfiguration
    {
        /// <summary>
        ///     Gets the path to the database.
        /// </summary>
        public AbsolutePath DatabaseFilePath { get; private set; }

        /// <summary>
        ///     Gets or sets a value indicating whether to use a shared connection vs. a connection per thread.
        /// </summary>
        public bool UseSharedConnection { get; set; } = true;

        /// <summary>
        ///     Gets or sets a value indicating whether or not to create a backup of the database after a successful startup.
        /// </summary>
        public bool BackupDatabase { get; set; } = false;

        /// <summary>
        ///     Gets or sets a value indicating whether to run an integrity check on the database after a successful startup.
        /// </summary>
        public bool VerifyIntegrityOnStartup { get; set; } = false;

        /// <summary>
        ///     Gets or sets the synchronization mode for writes to the database.
        /// </summary>
        public SynchronizationMode SyncMode { get; set; } = SynchronizationMode.Off;

        /// <summary>
        ///     Gets or sets the journal mode for writes to the database.
        /// </summary>
        public JournalMode JournalMode { get; set; } = JournalMode.WAL;

        // Private default constructor to ensure that callers must use the public constructor.
        // ReSharper disable once UnusedMember.Local
        private SQLiteDatabaseConfiguration()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SQLiteDatabaseConfiguration"/> class.
        /// </summary>
        public SQLiteDatabaseConfiguration(AbsolutePath path)
        {
            DatabaseFilePath = path;
        }

        /// <summary>
        ///     Returns the current config with the given database path override.
        /// </summary>
        /// <remarks>
        ///     Primarily used for overriding the path as it's passed down into a base class.
        /// </remarks>
        public SQLiteDatabaseConfiguration WithDatabasePath(AbsolutePath path)
        {
            DatabaseFilePath = path;
            return this;
        }
    }
}
