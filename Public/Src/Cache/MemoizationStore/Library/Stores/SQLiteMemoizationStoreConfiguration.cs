// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.SQLite;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <summary>
    ///     Grouped configuration for <see cref="SQLiteMemoizationStore"/>
    /// </summary>
    public class SQLiteMemoizationStoreConfiguration : SQLiteDatabaseConfiguration
    {
        /// <summary>
        ///     Gets or sets the maximum number of rows in the database.
        /// </summary>
        public long MaxRowCount { get; set; } = 500000;

        /// <summary>
        ///     Gets or sets a value indicating whether to wait for lru to complete on shutdown.
        /// </summary>
        public bool WaitForLruOnShutdown { get; set; } = true;

        /// <summary>
        ///     Gets or sets how long to wait for the single instance mutex/lockfile before giving up.
        /// </summary>
        public int SingleInstanceTimeoutSeconds { get; set; } = ContentStoreConfiguration.DefaultSingleInstanceTimeoutSeconds;

        /// <summary>
        /// Initializes a new instance of the <see cref="SQLiteMemoizationStoreConfiguration"/> class.
        /// </summary>
        public SQLiteMemoizationStoreConfiguration(AbsolutePath path)
            : base(path)
        {
        }
    }
}
