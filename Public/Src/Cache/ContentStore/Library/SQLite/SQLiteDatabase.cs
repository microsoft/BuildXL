// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Synchronization.Internal;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.SQLite
{
    /// <summary>
    ///     Shared implementation for all SQLite databases.
    /// </summary>
    public class SQLiteDatabase<TTracer> : IStartupShutdown
        where TTracer : Tracer, ISQLiteDatabaseTracer
    {
        private const string DatabaseBackupSuffix = ".bak";
        private const string DatabaseBackupTempSuffix = ".bak.tmp";
        private const string DatabaseInUseMarkerFileName = "SqliteDatabaseInUse.sem";

        // SQLite allows only a single writer at any instant.
        // Current usage is on a shared connection.
        // Multiple concurrent transactions on the same connection are not supported.
        // Therefore, we just use our own lock above SQLite on write paths.
        private readonly SemaphoreSlim _writerLock = new SemaphoreSlim(1, 1);
        private readonly SQLiteDatabaseConfiguration _config;
        private readonly ThreadLocal<SQLiteConnection> _connectionPerThread;
        private readonly SQLiteConnection _connection;
        private readonly string _connectionString;
        private BackgroundWorkerBase _backgroundWorker;
        private Thread _backgroundThread;
        private bool _disposed;

        /// <summary>
        ///     Store tracer.
        /// </summary>
        protected readonly TTracer Tracer;

        /// <summary>
        ///     Requests
        /// </summary>
        protected readonly BlockingCollection<RequestMessage> Messages = new BlockingCollection<RequestMessage>();

        /// <summary>
        ///     Base background worker implementation.
        /// </summary>
        protected class BackgroundWorkerBase
        {
            private readonly TTracer _tracer;

            /// <summary>
            ///     Initializes a new instance of the <see cref="BackgroundWorkerBase"/> class.
            /// </summary>
            public BackgroundWorkerBase(TTracer tracer)
            {
                _tracer = tracer;
            }

            /// <summary>
            ///     Process a background message on the background thread.
            /// </summary>
            public virtual void ProcessBackgroundMessage(Context context, RequestMessage message)
            {
                _tracer.Error(context, "Unprocessed message type");
            }

            /// <summary>
            ///     Do some background work on the background thread.
            /// </summary>
            // ReSharper disable once UnusedParameter.Global
            public virtual bool DoBackgroundWork(Context context, bool shutdown, bool sync)
            {
                return false;
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="SQLiteDatabase{TTracer}"/> class.
        /// </summary>
        protected SQLiteDatabase(
            Func<TTracer> tracer,
            SQLiteDatabaseConfiguration config)
        {
            Contract.Requires(tracer != null);
            Contract.Requires(config != null);
            Contract.Requires(config.DatabaseFilePath != null);
            Contract.Requires(config.DatabaseFilePath.Parent != null);
            Contract.Requires(config.DatabaseFilePath.Parent.Path.Length > 0);

            Tracer = tracer();
            _config = config;
            DatabaseBackupPath = new AbsolutePath(config.DatabaseFilePath.Path + DatabaseBackupSuffix);
            DatabaseBackupTempPath = new AbsolutePath(config.DatabaseFilePath.Path + DatabaseBackupTempSuffix);
            DatabaseInUseMarkerPath = config.DatabaseFilePath.Parent / DatabaseInUseMarkerFileName;

            Directory.CreateDirectory(config.DatabaseFilePath.Parent.Path);

            // Need a workaround for UNC paths (http://system.data.sqlite.org/index.html/info/bbdda6eae2)
            var dbPath = DatabaseFilePath.IsUnc ? @"\\" + DatabaseFilePath.Path : DatabaseFilePath.Path;
            _connectionString = $"Data Source={dbPath};Version=3";

            if (config.UseSharedConnection)
            {
                _connection = new SQLiteConnection(_connectionString);
            }
            else
            {
                _connectionPerThread = new ThreadLocal<SQLiteConnection>(CreateConnection, true);
            }
        }

        /// <summary>
        ///     Gets expected database version number.
        /// </summary>
        private int SchemaVersion => 1;

        /// <inheritdoc />
        public bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <summary>
        ///     Gets path to the database file.
        /// </summary>
        protected AbsolutePath DatabaseFilePath => _config.DatabaseFilePath;

        /// <summary>
        ///     Gets path to the database backup.
        /// </summary>
        protected AbsolutePath DatabaseBackupPath { get; }

        /// <summary>
        ///     Gets path to the database temporary backup.
        /// </summary>
        protected AbsolutePath DatabaseBackupTempPath { get; }

        /// <summary>
        ///     Gets path to the database marker file.
        /// </summary>
        protected AbsolutePath DatabaseInUseMarkerPath { get; }

        /// <summary>
        ///     Gets get a connection.
        /// </summary>
        protected SQLiteConnection Connection => _config.UseSharedConnection ? _connection : _connectionPerThread.Value;

        /// <summary>
        ///     Helper to build path to database filename.
        /// </summary>
        protected static AbsolutePath MakeDatabasePath(AbsolutePath path, string defaultFileName)
        {
            return Directory.Exists(path.Path) ? new AbsolutePath(Path.Combine(path.Path, defaultFileName)) : path;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Dispose(true);
            GC.SuppressFinalize(this);

            _disposed = true;
        }

        private void CloseConnections()
        {
            if (_config.UseSharedConnection)
            {
                _connection.Close();
            }
            else
            {
                foreach (SQLiteConnection connection in _connectionPerThread.Values)
                {
                    connection.Close();
                }
            }
        }

        /// <summary>
        ///     Protected implementation of Dispose pattern.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            CloseConnections();

            if (_config.UseSharedConnection)
            {
                _connection.Dispose();
            }
            else
            {
                foreach (SQLiteConnection connection in _connectionPerThread.Values)
                {
                    connection.Dispose();
                }

                _connectionPerThread.Dispose();
            }
        }

        private async Task<BoolResult> AttemptStartupAsync(Context context)
        {
            Tracer.Info(context, $"Starting database=[{_connectionString}]");
            if (_config.UseSharedConnection)
            {
                await _connection.OpenAsync();
            }

            await InitializeDatabaseAsync(context);
            await PostInitializeDatabaseAsync(context).IgnoreFailure();

            _backgroundWorker = CreateBackgroundWorker();
            _backgroundThread = new Thread(() => HandleBackgroundRequests(context.Logger));
            _backgroundThread.Start();

            StartupCompleted = true;

            return BoolResult.Success;
        }

        private void CreateMarkerFile(Context context)
        {
            if (File.Exists(DatabaseInUseMarkerPath.Path))
            {
                // The previous cache was interrupted before it could cleanup the
                // in-use marker, which means the cache may be in a corrupted state.
                // Force an integrity check on the database before using it.
                // Note: This assumes that there is only Cache instance writing to
                // the current directory.
                _config.VerifyIntegrityOnStartup = true;
                Tracer.IncrementSQLiteMarkerLeftBehindCount(
                    context,
                    "Found a database-in-use marker file that has been left behind from a previous run.");
            }
            else
            {
                try
                {
                    File.WriteAllBytes(DatabaseInUseMarkerPath.Path, new byte[] { });
                    Tracer.IncrementSQLiteMarkerCreatedCount(context, "Created a database-in-use marker file.");
                }
                catch (Exception exception)
                {
                    Tracer.IncrementSQLiteMarkerCreateFailedCount(context, exception, "Failed to create a database-in-use marker file");
                }
            }
        }

        /// <inheritdoc />
        public virtual Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;

            return StartupCall<TTracer>.RunAsync(Tracer, context, async () =>
            {
                var preStartupResult = await PreStartupAsync(context);
                if (!preStartupResult.Succeeded)
                {
                    return preStartupResult;
                }

                CreateMarkerFile(context);

                if (_config.BackupDatabase && File.Exists(DatabaseFilePath.Path))
                {
                    try
                    {
                        File.Copy(DatabaseFilePath.Path, DatabaseBackupTempPath.Path, overwrite: true);
                        Tracer.Info(context, "Created a temporary backup of the master DB");
                    }
                    catch (Exception e)
                    {
                        Tracer.Error(context, e, "Failed to create a copy of the master DB as backup");
                    }
                }

                try
                {
                    BoolResult startupAttempt = await AttemptStartupAsync(context);

                    if (startupAttempt.Succeeded)
                    {
                        if (_config.BackupDatabase && File.Exists(DatabaseBackupTempPath.Path))
                        {
                            try
                            {
                                File.Move(DatabaseBackupTempPath.Path, DatabaseBackupPath.Path); // rename
                                Tracer.Info(context, "The temporary backup DB has been upgraded to be the primary backup");
                            }
                            catch (Exception e)
                            {
                                Tracer.Error(context, e, "Failed to upgrade the temporary backup DB to primary backup");
                            }
                        }
                    } // else leave the temp backup file lying around for possible inspection. It may get overwritten at next startup.

                    return startupAttempt;
                }
                catch (SQLiteException sqle1)
                {
                    // Leave the temp backup file lying around for possible inspection. It may get overwritten at next startup.
                    if (sqle1.ResultCode != SQLiteErrorCode.Corrupt && sqle1.ResultCode != SQLiteErrorCode.NotADb)
                    {
                        throw;
                    }
                    else
                    {
                        Tracer.IncrementMasterDBCorruptionCount(context, "The master DB is corrupt.");
                        CloseConnections();

                        bool masterDbRestoredFromBackup = false;
                        if (File.Exists(DatabaseBackupPath.Path))
                        {
                            Tracer.Info(context, "Found the backup DB");
                            try
                            {
                                File.Copy(DatabaseBackupPath.Path, DatabaseFilePath.Path, overwrite: true);
                                masterDbRestoredFromBackup = true;
                                Tracer.Info(context, "Replaced the master DB with the backup");
                            }
                            catch (Exception e)
                            {
                                masterDbRestoredFromBackup = false;
                                Tracer.Error(context, e, "Restoring the master DB from backup failed");
                            }
                        }
                        else
                        {
                            masterDbRestoredFromBackup = false;
                            Tracer.Info(context, "Backup DB does not exist");
                        }

                        if (!masterDbRestoredFromBackup)
                        {
                            try
                            {
                                File.Delete(DatabaseFilePath.Path);
                                Tracer.Info(context, "Deleted corrupt master DB.");
                            }
                            catch (Exception e)
                            {
                                Tracer.Error(context, e, "Failed to delete master DB");
                            }
                        }

                        try
                        {
                            return await AttemptStartupAsync(context);
                        }
                        catch (SQLiteException sqle2)
                        {
                            if (sqle2.ResultCode != SQLiteErrorCode.Corrupt && sqle2.ResultCode != SQLiteErrorCode.NotADb)
                            {
                                throw;
                            }
                            else
                            {
                                Tracer.IncrementBackupDBCorruptionCount(context, "The backup DB is corrupt.");
                                CloseConnections();

                                try
                                {
                                    File.Delete(DatabaseFilePath.Path);
                                    Tracer.Info(context, "Deleted corrupt master DB");
                                }
                                catch (Exception e)
                                {
                                    Tracer.Error(context, e, "Failed to delete master DB");
                                }

                                try
                                {
                                    File.Delete(DatabaseBackupPath.Path);
                                    Tracer.Info(context, "Deleted corrupt backup DB");
                                }
                                catch (Exception e)
                                {
                                    Tracer.Error(context, e, "Failed to delete backup DB");
                                }

                                return await AttemptStartupAsync(context);
                            }
                        }
                    }
                }
            });
        }

        /// <summary>
        ///     Create specific (if overridden) or default background worker.
        /// </summary>
        protected virtual BackgroundWorkerBase CreateBackgroundWorker()
        {
            return new BackgroundWorkerBase(Tracer);
        }

        /// <summary>
        ///     Hook for subclass to process before startup.
        /// </summary>
        protected virtual Task<BoolResult> PreStartupAsync(Context context)
        {
            return Task.FromResult(BoolResult.Success);
        }

        /// <summary>
        ///     Hook for subclass to initialize.
        /// </summary>
        // ReSharper disable once UnusedParameter.Global
        protected virtual Task<BoolResult> PostInitializeDatabaseAsync(Context context)
        {
            return Task.FromResult(BoolResult.Success);
        }

        private void DeleteMarkerFile(Context context)
        {
            try
            {
                File.Delete(DatabaseInUseMarkerPath.Path);
                Tracer.IncrementSQLiteMarkerDeletedCount(context, "Deleted the database-in-use marker file.");
            }
            catch (Exception exception)
            {
                Tracer.IncrementSQLiteMarkerDeleteFailedCount(context, exception, "Failed to delete the database-in-use marker file");

                // TODO: What happens if we cannot delete the file? A sticky in-use marker (bug 1365340)
                // file will force the cache to always check integrity on startup, wasting
                // valuable seconds.
            }
        }

        /// <inheritdoc />
        public virtual Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;

            return ShutdownCall<TTracer>.RunAsync(Tracer, context, async () =>
            {
                await PreShutdownAsync(context);

                Messages.Add(ShutdownMessage.Instance);
                _backgroundThread?.Join();

                DeleteMarkerFile(context);

                await PostShutdownAsync(context).ThrowIfFailure();
                ShutdownCompleted = true;
                return BoolResult.Success;
            });
        }

        /// <summary>
        ///     Hook for subclass to process before shutdown.
        /// </summary>
        protected virtual Task PreShutdownAsync(Context context)
        {
            return Task.FromResult(0);
        }

        /// <summary>
        ///     Hook for subclass to process after shutdown.
        /// </summary>
        // ReSharper disable once UnusedParameter.Global
        protected virtual Task<BoolResult> PostShutdownAsync(Context context)
        {
            return Task.FromResult(BoolResult.Success);
        }

        /// <summary>
        ///     Checks whether the table has a column with the given name.
        /// </summary>
        protected async Task<bool> ColumnExistsAsync(string tableName, string columnName)
        {
            var enumerateColumnsCommand = $"PRAGMA table_info({tableName})";

            bool exists = false;
            await ExecuteReaderAsync("ColumnExists", enumerateColumnsCommand, reader =>
            {
                if (((string)reader["Name"]).Equals(columnName))
                {
                    exists = true;
                    return ReadResponse.StopReadingIgnoreFurther;
                }

                return ReadResponse.ContinueReading;
            });

            return exists;
        }

        private T RunInBlock<T>(Func<T> func, bool useTransaction)
        {
            T result;
            SQLiteTransaction transaction = useTransaction ? Connection.BeginTransaction(IsolationLevel.Serializable) : null;

            using (transaction)
            {
                result = func();

                if (useTransaction)
                {
                    transaction.Commit();
                }
            }

            return result;
        }

        private async Task<T> RunInBlockAsync<T>(Func<Task<T>> func, bool useTransaction)
        {
            T result;
            SQLiteTransaction transaction = useTransaction ? Connection.BeginTransaction(IsolationLevel.Serializable) : null;

            using (transaction)
            {
                result = await func();

                if (useTransaction)
                {
                    transaction.Commit();
                }
            }

            return result;
        }

        /// <summary>
        ///     Run a function delegate concurrently with others.
        /// </summary>
        protected T RunConcurrent<T>(Func<T> func)
        {
            return RunInBlock(func, false);
        }

        /// <summary>
        ///     Run a function delegate concurrently with others.
        /// </summary>
        protected Task<T> RunConcurrentAsync<T>(Func<Task<T>> func)
        {
            return RunInBlockAsync(func, false);
        }

        /// <summary>
        ///     Run a function delegate exclusively from any others.
        /// </summary>
        protected async Task<T> RunExclusiveAsync<T>(Func<Task<T>> func)
        {
             using (await _writerLock.WaitToken())
            {
                return await RunInBlockAsync(func, true);
            }
        }

        /// <summary>
        ///     Run an action delegate exclusively from any others.
        /// </summary>
        protected async Task RunExclusiveAsync(Func<Task> action)
        {
             using (await _writerLock.WaitToken())
            {
                Func<Task<bool>> func = async () =>
                {
                    await action();
                    return true;
                };
                await RunInBlockAsync(func, true);
            }
        }

        private async Task<T> RunExclusiveAsyncNoTransactionAsync<T>(Func<Task<T>> func)
        {
             using (await _writerLock.WaitToken())
            {
                return await RunInBlockAsync(func, false);
            }
        }

        /// <summary>
        ///     Execute a SQL non-query command.
        /// </summary>
        [SuppressMessage("Microsoft.Security", "CA2100:Review SQL queries for security vulnerabilities")]
        protected virtual async Task<int> ExecuteNonQueryAsync(string name, string command, params SQLiteParameter[] parameters)
        {
            Contract.Requires(name != null);
            Contract.Requires(command != null);

            using (var cmd = new SQLiteCommand(command, Connection))
            {
                cmd.Parameters.AddRange(parameters);
                return await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        ///     Create a CommandPool for pooling non-query commands.
        /// </summary>
        protected CommandPool<int> CreateNonQueryCommandPool(string commandText)
        {
            return new CommandPool<int>(Connection, commandText, command => command.ExecuteNonQueryAsync());
        }

        /// <summary>
        ///     Execute a SQL scalar command.
        /// </summary>
        [SuppressMessage("Microsoft.Security", "CA2100:Review SQL queries for security vulnerabilities")]
        protected virtual async Task<object> ExecuteScalarAsync(string name, string command, params SQLiteParameter[] parameters)
        {
            Contract.Requires(name != null);
            Contract.Requires(command != null);

            using (var cmd = new SQLiteCommand(command, Connection))
            {
                cmd.Parameters.AddRange(parameters);
                return await cmd.ExecuteScalarAsync();
            }
        }

        /// <summary>
        ///     Create a CommandPool for pooling scalar commands.
        /// </summary>
        protected CommandPool<object> CreateScalarCommandPool(string commandText)
        {
            return new CommandPool<object>(Connection, commandText, command => command.ExecuteScalarAsync());
        }

        /// <summary>
        ///     Read function return disposition toward opened SQLite data reader.
        /// </summary>
        protected enum ReadResponse
        {
            /// <summary>
            ///     Continue reading as more data can be accepted.
            /// </summary>
            ContinueReading,

            /// <summary>
            ///     Stop reading, possibly before end of data.
            /// </summary>
            StopReadingIgnoreFurther,

            /// <summary>
            ///     Stop reading with the expectation there is no more data to read.
            /// </summary>
            StopReadingAssertEnd
        }

        /// <summary>
        ///     Execute a SQL reader command.
        /// </summary>
        protected virtual Task<bool> ExecuteReaderAsync(
            string name,
            string command,
            Func<SQLiteDataReader, ReadResponse> readFunc,
            params SQLiteParameter[] parameters)
        {
            Contract.Requires(name != null);
            Contract.Requires(command != null);

            return ExecuteReaderImplAsync(command, readFunc, parameters);
        }

        [SuppressMessage("Microsoft.Security", "CA2100:Review SQL queries for security vulnerabilities")]
        private async Task<bool> ExecuteReaderImplAsync(string command, Func<SQLiteDataReader, ReadResponse> readFunc, params SQLiteParameter[] parameters)
        {
            using (var cmd = new SQLiteCommand(command, Connection))
            {
                cmd.Parameters.AddRange(parameters);
#pragma warning disable AsyncFixer02 // Long running or blocking operations under an async method
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    var done = false;

                    while (!done && await reader.ReadAsync())
                    {
                        var response = readFunc(reader);
                        switch (response)
                        {
                            case ReadResponse.ContinueReading:
                                continue;
                            case ReadResponse.StopReadingIgnoreFurther:
                                done = true;
                                break;
                            case ReadResponse.StopReadingAssertEnd:
                                Contract.Assert(!reader.Read());
#pragma warning restore AsyncFixer02 // Long running or blocking operations under an async method
                                break;
                            default:
                                throw ContractUtilities.AssertFailure("unexpected ReadResponse");
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        ///     Execute an EXPLAIN QUERY PLAN for the given command with optional parameters.
        /// </summary>
        protected string Explain(string command, params SQLiteParameter[] parameters)
        {
            var plan = new StringBuilder();

            ExecuteReaderImplAsync(
                "EXPLAIN QUERY PLAN " + command,
                reader =>
                {
                    for (var col = 0; col < reader.FieldCount; col++)
                    {
                        if (col > 0)
                        {
                            plan.Append('|');
                        }

                        plan.Append(reader[col]);
                    }
                    plan.AppendLine();
                    return ReadResponse.ContinueReading;
                },
                parameters
                ).GetAwaiter().GetResult();

            return plan.ToString();
        }

        private SQLiteConnection CreateConnection()
        {
            var connection = new SQLiteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private async Task RunIntegrityCheckAsync(Context context)
        {
            try
            {
                System.IO.FileInfo dbFileInfo = new System.IO.FileInfo(DatabaseFilePath.Path);
                Tracer.RecordSQLiteDatabaseSize(context, dbFileInfo.Length);

                // Getting file stats is supposed to be safe and should not interfere
                // with future file opens and reads.
            }
            catch (Exception exception)
            {
                Tracer.Error(context, exception, "Could not get file size of master DB");
            }

            try
            {
                Tracer.SQLiteIntegrityCheckStart(context);
                var integrityCheckWatch = Stopwatch.StartNew();
                await ExecuteReaderAsync(string.Empty, "PRAGMA integrity_check", reader =>
                {
                     const string integrityCheckOk = "ok";
                     var result = (string)reader[0]; // The reader should always have something to read, by design
                     if (result != integrityCheckOk)
                     {
                         integrityCheckWatch.Stop();
                         SQLiteException sqle = new SQLiteException(
                             SQLiteErrorCode.Corrupt,
                             $"Sqlite integrity check result expected to be [{integrityCheckOk}] but is instead [{result}]");
                         Tracer.SQLiteIntegrityCheckStopAtFailure(context, sqle, integrityCheckWatch.Elapsed);
                         throw sqle;
                     }

                     integrityCheckWatch.Stop();
                     Tracer.SQLiteIntegrityCheckStopAtSuccess(context, integrityCheckWatch.Elapsed);
                     return ReadResponse.StopReadingIgnoreFurther;
                });
            }
            catch (Exception exception)
            {
                SQLiteException sqle = exception as SQLiteException;
                if (sqle != null && sqle.ResultCode == SQLiteErrorCode.Corrupt)
                {
                    throw sqle;
                }

                // else ignore and continue. Treat other exceptions as a failure to perform the check and not
                // a real integrity check failure.
                Tracer.Error(context, exception, "Unable to perform an integrity check");
            }
        }

        private async Task InitializeDatabaseAsync(Context context)
        {
            await RunExclusiveAsyncNoTransactionAsync(async () =>
            {
                await ExecuteNonQueryAsync(string.Empty, $"PRAGMA journal_mode={_config.JournalMode};PRAGMA synchronous={_config.SyncMode};PRAGMA foreign_keys=ON;PRAGMA locking_mode=EXCLUSIVE;");
                
                if (_config.VerifyIntegrityOnStartup)
                {
                    await RunIntegrityCheckAsync(context);
                }

                await ExecuteNonQueryAsync(string.Empty, "CREATE TABLE IF NOT EXISTS dbversion (currentVersion INT)");
                object versionDbObject = await ExecuteScalarAsync(string.Empty, "SELECT currentVersion FROM dbversion");

                if (versionDbObject == null)
                {
                    const string text = "INSERT INTO dbversion (currentVersion) VALUES (@currentVersion)";
                    await ExecuteNonQueryAsync(string.Empty, text, new SQLiteParameter("@currentVersion", SchemaVersion));
                    versionDbObject = SchemaVersion;
                }

                var dbSchemaVersion = (int)versionDbObject;

                if (dbSchemaVersion != SchemaVersion)
                {
                    throw new ArgumentException("Database version mismatch.");
                }

                return dbSchemaVersion;
            });
        }

        private void HandleBackgroundRequests(ILogger logger)
        {
            var context = new Context(logger);

            try
            {
                Tracer.Debug(context, "Background thread start");

                var waitTimeSlow = TimeSpan.FromMilliseconds(-1);
                var waitTimeFast = TimeSpan.FromMilliseconds(50);
                var waitTime = waitTimeSlow;
                var work = false;
                var exit = false;

                while (!exit)
                {
                    RequestMessage message;
                    SyncMessage sync = null;

                    if (Messages.TryTake(out message, waitTime))
                    {
                        if (message is ShutdownMessage)
                        {
                            exit = true;
                        }
                        else if ((sync = message as SyncMessage) != null)
                        {
                        }
                        else
                        {
                            _backgroundWorker.ProcessBackgroundMessage(context, message);
                            work = true;
                        }
                    }

                    if (work || exit || sync != null)
                    {
                        work = _backgroundWorker.DoBackgroundWork(context, exit, sync != null);
                    }

                    sync?.Complete(true);
                    waitTime = work ? waitTimeFast : waitTimeSlow;
                }

                Tracer.Debug(context, "Background thread stop");
            }
            catch (Exception exception)
            {
                Tracer.Error(context, $"Background thread unexpected exception=[{exception}]");
            }
        }

        /// <summary>
        ///     Base message.
        /// </summary>
        protected class RequestMessage
        {
        }

        private class ShutdownMessage : RequestMessage
        {
            public static readonly ShutdownMessage Instance = new ShutdownMessage();
        }

        /// <summary>
        ///     Complete all pending/background operations.
        /// </summary>
        public Task SyncAsync()
        {
            var syncRequest = new SyncMessage();
            Messages.Add(syncRequest);
            return syncRequest.WaitForCompleteAsync();
        }

        /// <summary>
        ///     SyncRequestValue
        /// </summary>
        protected class SyncMessage : RequestMessage
        {
            private readonly TaskSourceSlim<bool> _tcs = TaskSourceSlim.Create<bool>();

            /// <summary>
            ///     Signals the Request as being complete with the given success value.
            /// </summary>
            /// <param name="completeSuccessfully">Whether the request was successfully satisfied.</param>
            public void Complete(bool completeSuccessfully)
            {
                _tcs.SetResult(completeSuccessfully);
            }

            /// <summary>
            ///     Asynchronously waits until the request has been completed.
            /// </summary>
            /// <returns>A task which blocks until the Request is signaled as having completed successfully, unsuccessfully, or throws.</returns>
            public Task<bool> WaitForCompleteAsync()
            {
                return _tcs.Task;
            }
        }
    }
}
