// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stats;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.MemoizationStore.Tracing
{
    /// <inheritdoc />
    public class SQLiteMemoizationStoreTracer : MemoizationStoreTracer, ISQLiteDatabaseTracer
    {
        private const string SQLiteIntegrityCheckCallName = "SQLiteIntegrityCheckCall";

        private readonly Counter _masterDbCorruptionCount;
        private readonly Counter _backupDbCorruptionCount;
        private readonly Counter _sqliteMarkerCreatedCount;
        private readonly Counter _sqliteMarkerCreateFailedCount;
        private readonly Counter _sqliteMarkerDeletedCount;
        private readonly Counter _sqliteMarkerDeleteFailedCount;
        private readonly Counter _sqliteMarkerLeftBehindCount;
        private readonly Counter _sqliteIntegrityCheckSucceeded;
        private readonly Counter _sqliteIntegrityCheckFailed;

        private readonly CallCounter _sqliteIntegrityCheckCallCounter;

        private long _sqliteDatabaseSize;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SQLiteMemoizationStoreTracer"/> class.
        /// </summary>
        /// <param name="logger">Logger</param>
        /// <param name="name">Tracer Name</param>
        public SQLiteMemoizationStoreTracer(ILogger logger, string name)
            : base(logger, name)
        {
            Counters.Add(_masterDbCorruptionCount = new Counter("MasterDbCorruptionCount"));
            Counters.Add(_backupDbCorruptionCount = new Counter("BackupDbCorruptionCount"));
            Counters.Add(_sqliteMarkerCreatedCount = new Counter("SQLiteMarkerCreated"));
            Counters.Add(_sqliteMarkerCreateFailedCount = new Counter("SQLiteMarkerCreateFailed"));
            Counters.Add(_sqliteMarkerDeletedCount = new Counter("SQLiteMarkerDeleted"));
            Counters.Add(_sqliteMarkerDeleteFailedCount = new Counter("SQLiteMarkerDeleteFailed"));
            Counters.Add(_sqliteMarkerLeftBehindCount = new Counter("SQLiteMarkerLeftBehind"));
            Counters.Add(_sqliteIntegrityCheckSucceeded = new Counter("SQLiteIntegrityCheckSucceeded"));
            Counters.Add(_sqliteIntegrityCheckFailed = new Counter("SQLiteIntegrityCheckFailed"));

            CallCounters.Add(_sqliteIntegrityCheckCallCounter = new CallCounter(SQLiteIntegrityCheckCallName));

            _sqliteDatabaseSize = -1;
        }

        /// <inheritdoc />
        public void IncrementMasterDBCorruptionCount(Context context, string message)
        {
            _masterDbCorruptionCount.Increment();
            if (context.IsEnabled)
            {
                Debug(context, $"{message} {Name}.{_masterDbCorruptionCount.Name} {_masterDbCorruptionCount.Value}");
            }
        }

        /// <inheritdoc />
        public void IncrementBackupDBCorruptionCount(Context context, string message)
        {
            _backupDbCorruptionCount.Increment();
            if (context.IsEnabled)
            {
                Debug(context, $"{message} {Name}.{_backupDbCorruptionCount.Name} {_backupDbCorruptionCount.Value}");
            }
        }

        /// <inheritdoc />
        public void IncrementSQLiteMarkerCreatedCount(Context context, string message)
        {
            _sqliteMarkerCreatedCount.Increment();
            if (context.IsEnabled)
            {
                Debug(context, $"{message} {Name}.{_sqliteMarkerCreatedCount.Name} {_sqliteMarkerCreatedCount.Value}");
            }
        }

        /// <inheritdoc />
        public void IncrementSQLiteMarkerCreateFailedCount(Context context, Exception exception, string message)
        {
            _sqliteMarkerCreateFailedCount.Increment();
            if (context.IsEnabled)
            {
                Error(context, exception, $"{message} {Name}.{_sqliteMarkerCreateFailedCount.Name} {_sqliteMarkerCreateFailedCount.Value}");
            }
        }

        /// <inheritdoc />
        public void IncrementSQLiteMarkerDeletedCount(Context context, string message)
        {
            _sqliteMarkerDeletedCount.Increment();
            if (context.IsEnabled)
            {
                Debug(context, $"{message} {Name}.{_sqliteMarkerDeletedCount.Name} {_sqliteMarkerDeletedCount.Value}");
            }
        }

        /// <inheritdoc />
        public void IncrementSQLiteMarkerDeleteFailedCount(Context context, Exception exception, string message)
        {
            _sqliteMarkerDeleteFailedCount.Increment();
            if (context.IsEnabled)
            {
                Error(context, exception, $"{message} {Name}.{_sqliteMarkerDeleteFailedCount.Name} {_sqliteMarkerDeleteFailedCount.Value}");
            }
        }

        /// <inheritdoc />
        public void IncrementSQLiteMarkerLeftBehindCount(Context context, string message)
        {
            _sqliteMarkerLeftBehindCount.Increment();
            if (context.IsEnabled)
            {
                Debug(context, $"{message} {Name}.{_sqliteMarkerLeftBehindCount.Name} {_sqliteMarkerLeftBehindCount.Value}");
            }
        }

        /// <inheritdoc />
        public void SQLiteIntegrityCheckStart(Context context)
        {
            _sqliteIntegrityCheckCallCounter.Started();
            if (context.IsEnabled)
            {
                Debug(context, $"{Name}.{SQLiteIntegrityCheckCallName} start");
            }
        }

        /// <inheritdoc />
        public void SQLiteIntegrityCheckStopAtSuccess(Context context, TimeSpan elapsed)
        {
            _sqliteIntegrityCheckSucceeded.Increment();
            _sqliteIntegrityCheckCallCounter.Completed(elapsed.Ticks);
            if (context.IsEnabled)
            {
               Debug(context, $"{Name}.{SQLiteIntegrityCheckCallName} success {elapsed.TotalMilliseconds}ms, {Name}.{_sqliteIntegrityCheckSucceeded.Name} {_sqliteIntegrityCheckSucceeded.Value}");
            }
        }

        /// <inheritdoc />
        public void SQLiteIntegrityCheckStopAtFailure(Context context, Exception exception, TimeSpan elapsed)
        {
            _sqliteIntegrityCheckFailed.Increment();
            _sqliteIntegrityCheckCallCounter.Completed(elapsed.Ticks);
            if (context.IsEnabled)
            {
                Error(context, exception, $"{Name}.{SQLiteIntegrityCheckCallName} failed {elapsed.TotalMilliseconds}ms, {_sqliteIntegrityCheckFailed.Name} {_sqliteIntegrityCheckFailed.Value}");
            }
        }

        /// <inheritdoc />
        public void RecordSQLiteDatabaseSize(Context context, long size)
        {
            _sqliteDatabaseSize = size;
            if (context.IsEnabled)
            {
                Debug(context, $"Sqlite DB has {_sqliteDatabaseSize} bytes");
            }
        }

        /// <inheritdoc />
        public CounterSet GetSQLiteDatabaseCounters()
        {
            var counters = new CounterSet();
            counters.Add("SQLiteDatabaseSize", _sqliteDatabaseSize);
            return counters;
        }
    }
}
