// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.MemoizationStore.Stores;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    public class TestSQLiteMemoizationStore : SQLiteMemoizationStore
    {
        public TestSQLiteMemoizationStore(
            ILogger logger,
            IClock clock,
            SQLiteMemoizationStoreConfiguration config)
            : base(
                logger,
                clock,
                config)
        {
        }

        /// <summary>
        ///     Test hook for deleting a column from the given table.
        /// </summary>
        public Task DeleteColumnAsync(string tableName, string columnNameToDelete)
        {
            return RunExclusiveAsync(async () =>
            {
                var listColumnsCommand = $"pragma table_info({tableName})";
                var columnNamesToKeep = new List<string>();
                await ExecuteReaderAsync("ListColumns", listColumnsCommand, reader =>
                {
                    var columnName = (string)reader["Name"];
                    if (!columnName.Equals(columnNameToDelete))
                    {
                        columnNamesToKeep.Add(columnName);
                    }

                    return ReadResponse.ContinueReading;
                });
                var tempTableName = $"{tableName}_temp";
                await ExecuteNonQueryAsync("RenameToTemp", $"ALTER TABLE {tableName} RENAME TO {tempTableName}");
                await ExecuteNonQueryAsync(
                    "CreateNew",
                    $"CREATE TABLE {tableName} AS SELECT {string.Join(",", columnNamesToKeep)} FROM {tempTableName}");
                await ExecuteNonQueryAsync("DeleteTemp", $"DROP TABLE IF EXISTS {tempTableName}");
            });
        }

        public AbsolutePath DatabaseFilePathExtracted => DatabaseFilePath;

        public AbsolutePath DatabaseBackupPathExtracted => DatabaseBackupPath;

        public AbsolutePath DatabaseInUseMarkerFilePathExtracted => DatabaseInUseMarkerPath;
    }
}
