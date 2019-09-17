// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Security.Cryptography;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Threading;
using RocksDbSharp;

namespace BuildXL.Engine.Cache.KeyValueStores
{
    /// <summary>
    /// Manages and provides access to an <see cref="IBuildXLKeyValueStore"/>.
    /// This class abstracts away the underlying implementation of <see cref="IBuildXLKeyValueStore"/>.
    /// </summary>
    public partial class KeyValueStoreAccessor : IDisposable
    {
        /// <summary>
        /// Exceptions are divided into user-produced, and RocksDb-produced. Each handling mode differs in how it deals
        /// with the two kinds of exceptions.
        /// </summary>
        public enum ExceptionHandlingMode
        {
            /// <summary>
            /// Any exception produced within code that uses the key-value store will cause the store to shutdown,
            /// call the invalidation handler, call the failure handler, and return a failure containing the exception.
            /// 
            /// If the exception is user-produced, it is thrown for the user to handle.
            /// </summary>
            /// <remarks>
            /// This is the default due to backwards-compatibility concerns.
            /// </remarks>
            STRICT,
            /// <summary>
            /// RocksDb exceptions will cause the store to shutdown, call the invalidation handler, and return a 
            /// failure containing the exception.
            ///
            /// User exceptions will call the failure handler, and re-throw.
            /// </summary>
            RELAXED,
        }

        /// <summary>
        /// The file type extension used to denote storage files by the underlying <see cref="IBuildXLKeyValueStore"/>.
        /// Currently, a <see cref="RocksDbStore"/>.
        /// </summary>
        public static string StorageFileTypeExtension = ".sst";

        /// <summary>
        /// The log file name used by the underlying <see cref="IBuildXLKeyValueStore"/>.
        /// Currently, a <see cref= "RocksDbStore"/>.
        /// </summary>
        public static string LogFileName = "LOG";

        /// <summary>
        /// String that denotes files as outdated by the underlying <see cref="IBuildXLKeyValueStore"/>.
        /// Currently, a <see cref="RocksDbStore"/>.
        /// </summary>
        public static string OutdatedFileMarker = "old";

        /// <summary>
        /// Default column family name used by the underlying <see cref="IBuildXLKeyValueStore"/>. 
        /// This is equivalent to passing null as the column family name.
        /// Current, a <see cref="RocksDbStore"/>.
        /// </summary>
        public static string DefaultColumnName = ColumnFamilies.DefaultName;

        /// <summary>
        /// The key-value store.
        /// </summary>
        private readonly IBuildXLKeyValueStore m_store;

        /// <summary>
        /// The directory containing the key-value store.
        /// </summary>
        public string StoreDirectory { get; }

        /// <summary>
        /// Indicates the store should be marked as invalid at the time of disposing.
        /// Only done on dispose to reduce number of concurrent invalidations possible.
        /// </summary>
        private bool m_invalidateStoreOnDispose = false;

        /// <summary>
        /// Whether the store was disposed.
        /// </summary>
        private bool m_disposed = false;

        /// <summary>
        /// Protects the store from being disposed while read/write operations are occurring and vice versa.
        /// </summary>
        private readonly ReadWriteLock m_rwl = ReadWriteLock.Create();

        /// <summary>
        /// <see cref="Failure"/> to return when this store has been disabled due to an error.
        /// </summary>
        private Failure m_disabledFailure;

        /// <summary>
        /// <see cref="Failure"/> to return when this store has been disposed.
        /// </summary>
        private static readonly Failure s_disposedFailure = new Failure<string>("Access to the key-value store has been disabled because the instance is already closed.");

        /// <summary>
        /// Whether the store was opened in read-only mode.
        /// </summary>
        public bool ReadOnly { get; }

        /// <summary>
        /// Determines when the failure handler will be called, when the store will be locked down because of an 
        /// error, and whether an exception should be rethrown.
        /// </summary>
        private readonly Func<Failure<Exception>, (bool lockdown, bool rethrow)> m_errorPolicy;

        /// <summary>
        /// Allows handling of exceptions.
        /// 
        /// See <see cref="ExceptionHandlingMode"/> for details.
        /// </summary>
        private readonly Action<Failure> m_failureHandler;

        /// <summary>
        /// Allows handling of exceptions that cause the store to be invalidated.
        /// 
        /// See <see cref="ExceptionHandlingMode"/> for details.
        /// </summary>
        private readonly Action<Failure> m_invalidationHandler;

        /// <summary>
        /// If true, the store was newly created when open was called; if false, an existing store was opened.
        /// </summary>
        public bool CreatedNewStore = false;

        #region Versioning

        /// <summary>
        /// The suffix of the file that contains versioning information of an <see cref="IBuildXLKeyValueStore"/> to check for compatibility.
        /// </summary>
        public const string VersionFileName = "KeyValueStoreVersion";

        /// <summary>
        /// <see cref="FileEnvelope"/> versioned on <see cref="AccessorVersionHash"/>, the file itself contains further versioning information about <see cref="StoreVersion"/>.
        /// </summary>
        private static readonly FileEnvelope s_fileEnvelope = new FileEnvelope(name: VersionFileName, version: AccessorVersionHash);

        /// <summary>
        /// The version of the <see cref="KeyValueStoreAccessor"/> represented by a string.
        /// This reflects the version of the underlying <see cref="IBuildXLKeyValueStore"/> implementation.
        /// </summary>
        /// <remarks>
        /// The string name of the underlying <see cref="IBuildXLKeyValueStore"/> is included to ensure that swapping
        /// the implementation causes a version change.
        /// </remarks>
        private static string AccessorVersionString => nameof(RocksDbStore) + RocksDbStore.Version.ToString();

        private static int? s_accessorVersionHash = null;
        
        /// <summary>
        /// Used for version comparisons, a hash of <see cref="AccessorVersionString"/> truncated to an int32 by taking the last 4 bytes.
        /// </summary>
        /// <remarks>
        /// This hash is (almost) guaranteed to be unique, but not necessarily increasing.
        /// Because the hash is truncated, there is an increased possibility of collision, but it is still very unlikely.
        /// </remarks>
        public static int AccessorVersionHash
        {
            get
            {
                if (!s_accessorVersionHash.HasValue)
                {
                    using (var hashAlgorithm = MD5.Create())
                    {
                        byte[] data = hashAlgorithm.ComputeHash(System.Text.Encoding.UTF8.GetBytes(AccessorVersionString));

                        // Take the last 4 bytes for a 32-bit int
                        s_accessorVersionHash = Math.Abs(BitConverter.ToInt32(data, data.Length - 4));
                    }
                }

                return s_accessorVersionHash.Value;
            }
        }

        /// <summary>
        /// Value that indicates the store has no versioning or the versioning should be ignored when opening the store.
        /// Useful for attempting to extract information from outdated stores.
        /// </summary>
        public const int IgnoreStoreVersion = VersionConstants.IgnoreStore;

        /// <summary>
        /// Constants used to mark particular non-standard states of the store version number.
        /// These version numbers should not be used outside of this <see cref="KeyValueStoreAccessor"/>.
        /// </summary>
        private struct VersionConstants
        {
            /// <summary>
            /// Value that indicates the store has no versioning or the versioning should be ignored when opening the store.
            /// Note that in addition to any versioning passed during <see cref="KeyValueStoreAccessor.OpenWithVersioning(string, int, bool, IEnumerable{string}, IEnumerable{string}, Action{Failure}, bool, bool, bool, bool, bool, Action{Failure}, ExceptionHandlingMode)"/>,
            /// all stores using <see cref="KeyValueStoreAccessor"/> are also inherently versioned on <see cref="AccessorVersionHash"/>.
            /// </summary>
            public const int IgnoreStore = -1;

            /// <summary>
            /// The int used to mark a store deemed invalid during runtime.
            /// </summary>
            public const int InvalidStore = int.MinValue;

            /// <summary>
            /// The int used to represent an unversioned store's version. This is represented on disk as a non-existent version file.
            /// </summary>
            public const int UnversionedStore = 0;
        }

        /// <summary>
        /// On successful open, the version number of the store opened; otherwise, -1.
        /// </summary>
        public int StoreVersion = -1;

        #endregion Versioning

        /// <summary>
        /// Opens or creates a key value store and returns a <see cref="KeyValueStoreAccessor"/> to the store.
        /// <see cref="OpenWithVersioning(string, int, bool, IEnumerable{string}, IEnumerable{string}, Action{Failure}, bool, bool, bool, bool, bool, Action{Failure}, ExceptionHandlingMode)"/>
        /// to open or create a versioned key value store.
        /// </summary>
        /// <param name="storeDirectory">
        /// The directory containing the key-value store.
        /// </param>
        /// <param name="defaultColumnKeyTracked">
        /// Whether the default column should be key-tracked. 
        /// This will create two columns for the same data,
        /// one with just keys and the other with key and value.
        /// </param>
        /// <param name="additionalColumns">
        /// The names of any additional column families in the key-value store.
        /// If no additonal column families are provided, all entries will be stored
        /// in the default column.
        /// Column families are analogous to tables in relational databases.
        /// </param>
        /// <param name="additionalKeyTrackedColumns">
        /// The names of any additional column families in the key-value store that
        /// should also be key-tracked. 
        /// This will create two columns for the same data,
        /// one with just keys and the other with key and value.
        /// Column families are analogous to tables in relational databases.
        /// </param>
        /// <param name="failureHandler">
        /// Allows for custom exception handling such as context-specific logging.
        /// </param>
        /// <param name="openReadOnly">
        /// Whether the store should be opened read-only.
        /// </param>
        /// <param name="dropMismatchingColumns">
        /// If a store already exists at the given directory, whether any columns that mismatch the the columns that were passed into the constructor
        /// should be dropped. This will cause data loss and can only be applied in read-write mode.
        /// </param>
        /// <param name="onFailureDeleteExistingStoreAndRetry">
        /// On failure to open an existing store at the given directory, whether an attempt to delete the existing store should be made
        /// to create a new one in its place. This will cause data loss of the old store.
        /// </param>
        /// <param name="rotateLogs">
        /// Have RocksDb rotate logs, useful for debugging performance issues. See <see cref="RocksDbStore"/> for details on this.
        /// </param>
        /// <param name="openBulkLoad">
        /// Have RocksDb open for bulk loading.
        /// </param>
        /// <param name="exceptionHandlingMode">
        /// <see cref="ExceptionHandlingMode"/>
        /// </param>
        /// <param name="invalidationHandler">
        /// <see cref="m_invalidationHandler"/>
        /// </param>
        public static Possible<KeyValueStoreAccessor> Open(
            string storeDirectory,
            bool defaultColumnKeyTracked = false,
            IEnumerable<string> additionalColumns = null,
            IEnumerable<string> additionalKeyTrackedColumns = null,
            Action<Failure> failureHandler = null,
            bool openReadOnly = false,
            bool dropMismatchingColumns = false,
            bool onFailureDeleteExistingStoreAndRetry = false,
            bool rotateLogs = false, 
            bool openBulkLoad = false,
            Action<Failure> invalidationHandler = null,
            ExceptionHandlingMode exceptionHandlingMode = ExceptionHandlingMode.STRICT)
        {
            return OpenWithVersioning(
                storeDirectory,
                VersionConstants.UnversionedStore,
                defaultColumnKeyTracked,
                additionalColumns,
                additionalKeyTrackedColumns,
                failureHandler,
                openReadOnly,
                dropMismatchingColumns,
                onFailureDeleteExistingStoreAndRetry,
                rotateLogs, 
                openBulkLoad,
                invalidationHandler,
                exceptionHandlingMode);
        }

        /// <summary>
        /// Opens or creates a versioned key value store and returns a <see cref="KeyValueStoreAccessor"/> to the store.
        /// </summary>
        /// <param name="storeDirectory">
        /// The directory containing the key-value store.
        /// </param>
        /// <param name="storeVersion">
        /// The version of the caller's store.
        /// </param>
        /// <param name="defaultColumnKeyTracked">
        /// Whether the default column should be key-tracked. 
        /// This will create two columns for the same data,
        /// one with just keys and the other with key and value.
        /// </param>
        /// <param name="additionalColumns">
        /// The names of any additional column families in the key-value store.
        /// If no additonal column families are provided, all entries will be stored
        /// in the default column.
        /// Column families are analogous to tables in relational databases.
        /// </param>
        /// <param name="additionalKeyTrackedColumns">
        /// The names of any additional column families in the key-value store that
        /// should also be key-tracked. 
        /// This will create two columns for the same data,
        /// one with just keys and the other with key and value.
        /// Column families are analogous to tables in relational databases.
        /// </param>
        /// <param name="failureHandler">
        /// Allows for custom exception handling such as context-specific logging.
        /// </param>
        /// <param name="openReadOnly">
        /// Whether the store should be opened read-only.
        /// </param>
        /// <param name="dropMismatchingColumns">
        /// If a store already exists at the given directory, whether any columns that mismatch the the columns that were passed into the constructor
        /// should be dropped. This will cause data loss and can only be applied in read-write mode.
        /// </param>
        /// <param name="onFailureDeleteExistingStoreAndRetry">
        /// On failure to open an existing store at the given directory, whether an attempt to delete the existing store should be made
        /// to create a new one in its place. This will cause data loss of the old store.
        /// </param>
        /// <param name="rotateLogs">
        /// Have RocksDb rotate logs, useful for debugging performance issues. See <see cref="RocksDbStore"/> for details on this.
        /// </param>
        /// <param name="openBulkLoad">
        /// Have RocksDb open for bulk loading.
        /// </param>
        /// <param name="exceptionHandlingMode">
        /// <see cref="ExceptionHandlingMode"/>
        /// </param>
        /// <param name="invalidationHandler">
        /// <see cref="m_invalidationHandler"/>
        /// </param>
        public static Possible<KeyValueStoreAccessor> OpenWithVersioning(
            string storeDirectory,
            int storeVersion,
            bool defaultColumnKeyTracked = false,
            IEnumerable<string> additionalColumns = null,
            IEnumerable<string> additionalKeyTrackedColumns = null,
            Action<Failure> failureHandler = null,
            bool openReadOnly = false,
            bool dropMismatchingColumns = false,
            bool onFailureDeleteExistingStoreAndRetry = false,
            bool rotateLogs = false, 
            bool openBulkLoad = false,
            Action<Failure> invalidationHandler = null,
            ExceptionHandlingMode exceptionHandlingMode = ExceptionHandlingMode.STRICT)
        {
            // First attempt
            var possibleAccessor = OpenInternal(
                    storeDirectory,
                    storeVersion,
                    defaultColumnKeyTracked,
                    additionalColumns,
                    additionalKeyTrackedColumns,
                    failureHandler,
                    openReadOnly,
                    dropMismatchingColumns,
                    createNew: !FileUtilities.DirectoryExistsNoFollow(storeDirectory),
                    rotateLogs: rotateLogs,
                    openBulkLoad: openBulkLoad,
                    invalidationHandler: invalidationHandler,
                    exceptionHandlingMode: exceptionHandlingMode
                    );

            if (!possibleAccessor.Succeeded 
                && onFailureDeleteExistingStoreAndRetry /* Fall-back on deleting the store and creating a new one */
                && !openReadOnly /* But only if there's write permissions (no point in reading from an empty store) */)
            {
                possibleAccessor = OpenInternal(
                    storeDirectory,
                    storeVersion,
                    defaultColumnKeyTracked,
                    additionalColumns,
                    additionalKeyTrackedColumns,
                    failureHandler,
                    openReadOnly,
                    dropMismatchingColumns,
                    createNew: true,
                    rotateLogs: rotateLogs,
                    openBulkLoad: openBulkLoad,
                    invalidationHandler: invalidationHandler,
                    exceptionHandlingMode: exceptionHandlingMode
                    );
            }

            return possibleAccessor;
        }

        private static Possible<KeyValueStoreAccessor> OpenInternal(
            string storeDirectory,
            int storeVersion,
            bool defaultColumnKeyTracked,
            IEnumerable<string> additionalColumns,
            IEnumerable<string> additionalKeyTrackedColumns,
            Action<Failure> failureHandler,
            bool openReadOnly,
            bool dropMismatchingColumns,
            bool createNew,
            bool rotateLogs,
            bool openBulkLoad,
            Action<Failure> invalidationHandler,
            ExceptionHandlingMode exceptionHandlingMode)
        {
            KeyValueStoreAccessor accessor = null;
            bool useVersioning = storeVersion != VersionConstants.IgnoreStore;

            try
            {
                var persistedStoreVersion = -1;
                if (createNew)
                {
                    accessor?.Dispose();

                    if (FileUtilities.FileExistsNoFollow(storeDirectory))
                    {
                        FileUtilities.DeleteFile(storeDirectory);
                    }
                    else if (FileUtilities.DirectoryExistsNoFollow(storeDirectory))
                    {
                        FileUtilities.DeleteDirectoryContents(storeDirectory);
                    }

                    FileUtilities.CreateDirectory(storeDirectory);

                    if (useVersioning)
                    {
                        WriteVersionFile(storeDirectory, storeVersion);
                    }

                    persistedStoreVersion = storeVersion;
                }
                else
                {
                    var possibleStoreVersion = ReadStoreVersion(storeDirectory);
                    if (possibleStoreVersion.Succeeded)
                    {
                        persistedStoreVersion = possibleStoreVersion.Result;
                        // Even if the store does not use the built in versioning, checks for an invalid store will be done to ensure a corrupt store is not opened
                        if (persistedStoreVersion == VersionConstants.InvalidStore)
                        {
                            return new Failure<string>("The existing store is invalid and and may not be safe to open.");
                        }

                        // First check for invalid (corrupt) stores before incompatible store format versions
                        if (useVersioning && persistedStoreVersion != storeVersion)
                        {
                            return new Failure<string>($"The existing store format version is incompatible expected format version. Existing store version: {persistedStoreVersion}, expected format version: {storeVersion}.");
                        }
                    }
                    else
                    {
                        return possibleStoreVersion.Failure;
                    }
                }

                accessor = new KeyValueStoreAccessor(
                    storeDirectory,
                    persistedStoreVersion,
                    defaultColumnKeyTracked,
                    additionalColumns,
                    additionalKeyTrackedColumns,
                    failureHandler,
                    openReadOnly,
                    dropMismatchingColumns,
                    createNew,
                    rotateLogs,
                    openBulkLoad,
                    invalidationHandler,
                    exceptionHandlingMode);
            }
            catch (Exception ex)
            {
                return new Failure<Exception>(ex);
            }

            return accessor;
        }

        /// <summary>
        /// Writes a file containing versioning information to the provided store directory.
        /// </summary>
        private static void WriteVersionFile(string storeDirectory, int storeVersion)
        {
            var versionFile = GetVersionFile(storeDirectory);
            using (var stream = FileUtilities.CreateFileStream(
                versionFile,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.Delete))
            {
                // We don't have anything in particular to correlate this file to,
                // so we are simply creating a unique correlation id that is used as part
                // of the header consistency check.
                var correlationId = FileEnvelopeId.Create();
                s_fileEnvelope.WriteHeader(stream, correlationId);

                using (var writer = new BuildXLWriter(debug: false, stream: stream, leaveOpen: true, logStats: false))
                {
                    writer.Write(storeVersion);
                }

                s_fileEnvelope.FixUpHeader(stream, correlationId);
            }
        }

        /// <summary>
        /// Reads a file containing versioning information from the provided store directory.
        /// </summary>
        private static Possible<int> ReadStoreVersion(string storeDirectory)
        {
            var versionFile = GetVersionFile(storeDirectory);
            if (!FileUtilities.FileExistsNoFollow(versionFile))
            {
                return VersionConstants.UnversionedStore;
            }

            using (var stream = FileUtilities.CreateFileStream(
                versionFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Delete))
            {
                try
                {
                    // Check that the accessor version is valid.
                    s_fileEnvelope.ReadHeader(stream);

                    // Check that the current store version must match the persisted store's version.
                    using (var reader = new BuildXLReader(debug: false, stream: stream, leaveOpen: false))
                    {
                        // Represents persisted store version
                        return reader.ReadInt32();
                    }
                }
                catch (Exception e)
                {
                    return new Failure<string>($"Error reading existing version file: {e.ToStringDemystified()}");
                }
            }
        }

        /// <summary>
        /// Given the store directory, provides the path to the file containing version information.
        /// </summary>
        private static string GetVersionFile(string storeDirectory)
        {
            return Path.Combine(storeDirectory, VersionFileName);
        }

        /// <summary>
        /// Writes a version file that indicates the store is invalid.
        /// </summary>
        private void InvalidateStore()
        {
            WriteVersionFile(StoreDirectory, VersionConstants.InvalidStore);
        }

        /// <summary>
        /// Create an instance by getting a snapshot from the given KeyValueStore accessor.
        /// </summary>
        public KeyValueStoreAccessor(KeyValueStoreAccessor accessor)
        {
            StoreDirectory = accessor.StoreDirectory;
            ReadOnly = true;
            StoreVersion = accessor.StoreVersion;
            CreatedNewStore = false;

            m_invalidateStoreOnDispose = accessor.m_invalidateStoreOnDispose;
            m_failureHandler = accessor.m_failureHandler;
            m_disabledFailure = accessor.m_disabledFailure;
            m_disposed = accessor.m_disposed;
            m_errorPolicy = accessor.m_errorPolicy;
            m_invalidationHandler = accessor.m_invalidationHandler;

            if (accessor.Disabled)
            {
                // Creating a snapshot of a disabled store will carry the error along, so it should never be accessed.
                m_store = null;
            } 
            else
            {
                m_store = accessor.m_store.CreateSnapshot();
            }
        }

        private KeyValueStoreAccessor(
            string storeDirectory,
            int storeVersion,
            bool defaultColumnKeyTracked,
            IEnumerable<string> additionalColumns,
            IEnumerable<string> additionalKeyTrackedColumns,
            Action<Failure> failureHandler,
            bool openReadOnly,
            bool dropColumns,
            bool createdNewStore,
            bool rotateLogs,
            bool openBulkLoad,
            Action<Failure> invalidationHandler,
            ExceptionHandlingMode exceptionHandlingMode)
        {
            Contract.Assert(storeVersion != VersionConstants.InvalidStore, "No store should pass the invalid store version since it is not safe to open an invalid store.");
            StoreDirectory = storeDirectory;
            ReadOnly = openReadOnly;
            StoreVersion = storeVersion;
            CreatedNewStore = createdNewStore;

            m_store = new RocksDbStore(
                StoreDirectory,
                defaultColumnKeyTracked,
                additionalColumns,
                additionalKeyTrackedColumns,
                openReadOnly,
                dropColumns,
                rotateLogs,
                openBulkLoad);

            m_failureHandler = failureHandler;
            m_invalidationHandler = invalidationHandler;
            
            switch (exceptionHandlingMode)
            {
                case ExceptionHandlingMode.RELAXED:
                    m_errorPolicy = RelaxedErrorPolicy;
                    break;
                case ExceptionHandlingMode.STRICT:
                    m_errorPolicy = StrictErrorPolicy;
                    break;
            }
        }

        /// <summary>
        /// Provides access to the underlying store.
        /// </summary>
        /// <remarks>
        /// Use `state` to avoid lambda capture.
        /// </remarks>
        /// <returns>
        /// On success, <see cref="Unit.Void"/>;
        /// on failure, a <see cref="Failure"/>.
        /// </returns>
        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions] // allows catching exceptions from unmanaged code
        public Possible<TResult> Use<TState, TResult>(Func<IBuildXLKeyValueStore, TState, TResult> use, TState state)
        {
            using (m_rwl.AcquireReadLock())
            {
                // All RocksDb usages are checked for exceptions:
                //  - If any error happens inside RocksDb, we assume the store is permanently corrupted. All 
                //    further calls will result in an error. The user-defined invalidation handler is called.
                //  - If an error happens within the user-provided function, but is not RocksDb related, then we 
                //    don't disable and throw.
                // In all cases, the user-defined failure handler is called.
                if (Disabled)
                {
                    return DisposedOrDisabledFailure;
                }

                try
                {
                    return use(m_store, state);
                }
                catch (Exception ex)
                {
                    var result = HandleException(ex, out var rethrow);
                    if (rethrow)
                    {
                        throw;
                    }

                    return result;
                }
            }
        }

        /// <summary>
        /// Provides access to the underlying store.
        /// </summary>
        /// <returns>
        /// On success, <see cref="Unit.Void"/>;
        /// on failure, a <see cref="Failure"/>.
        /// </returns>
        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions] // allows catching exceptions from unmanaged code
        public Possible<TResult> Use<TResult>(Func<IBuildXLKeyValueStore, TResult> use)
        {
            return Use(
                (store, propagatedUse) => propagatedUse(store),
                use);
        }

        /// <summary>
        /// Provides access to the underlying store.
        /// </summary>
        /// <param name="use">
        /// Function that take the store as a parameter.
        /// </param>
        /// <returns>
        /// On success, <see cref="Unit.Void"/>;
        /// on failure, a <see cref="Failure"/>.
        /// </returns>
        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions] // allows catching exceptions from unmanaged code
        public Possible<Unit> Use(Action<IBuildXLKeyValueStore> use)
        {
            return Use(
                (store, propagatedUse) =>
                {
                    propagatedUse(store);
                    return Unit.Void;
                }, use);
        }
        
        private (bool lockdown, bool rethrow) StrictErrorPolicy(Failure<Exception> failure)
        {
            Contract.Requires(failure != null);

            var ex = failure.Content;
            var isUserError = true;
            if (ex is RocksDbSharpException || ex is System.Runtime.InteropServices.SEHException)
            {
                // The SEHException class handles SEH (structured exception handling) errors that are thrown from 
                // unmanaged code, but that have not been mapped to another .NET Framework exception. The SEHException
                // class also corresponds to the HRESULT E_FAIL (0x80004005).
                isUserError = false;
            }

            m_failureHandler?.Invoke(failure);
            return (true, isUserError);
        }

        private (bool lockdown, bool rethrow) RelaxedErrorPolicy(Failure<Exception> failure)
        {
            Contract.Requires(failure != null);

            var ex = failure.Content;

            var isRocksDbError = false;
            if (ex is RocksDbSharpException || ex is System.Runtime.InteropServices.SEHException)
            {
                // The SEHException class handles SEH (structured exception handling) errors that are thrown from 
                // unmanaged code, but that have not been mapped to another .NET Framework exception. The SEHException
                // class also corresponds to the HRESULT E_FAIL (0x80004005).
                isRocksDbError = true;
            }

            if (isRocksDbError)
            {
                return (true, false);
            } 
            else
            {
                m_failureHandler?.Invoke(failure);
                return (false, true);
            }
        }

        private Failure<Exception> HandleException(Exception ex, out bool rethrow)
        {
            Contract.Requires(ex != null);

            var failure = new Failure<Exception>(ex);

            var status = m_errorPolicy(failure);
            rethrow = status.rethrow;
            if (status.lockdown)
            {
                InvalidateStore(failure);
            }

            return failure;
        }

        private void InvalidateStore(Failure<Exception> failure)
        {
            m_disabledFailure = new Failure<string>("Access to the key-value store has been disabled because of the error.", failure);

            // Any store-implementation related exceptions are conservatively assumed to indicate an invalid or corrupted store
            m_invalidateStoreOnDispose = true;

            m_invalidationHandler?.Invoke(failure);
        }

        /// <summary>
        /// Lists all existing column families.
        /// </summary>
        public static IEnumerable<string> ListColumnFamilies(string storeDirectory)
        {
            IEnumerable<string> result = CollectionUtilities.EmptyArray<string>();
            try
            {
                result = RocksDb.ListColumnFamilies(new DbOptions(), storeDirectory);
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                // An exception is thrown if no store exists
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

            return result;
        }

        /// <summary>
        /// Finishes up/cleans remaining RocksDB tasks and flushes DB to disk.
        /// </summary>
        public void Dispose()
        {
            using (m_rwl.AcquireWriteLock())
            {
                if (!m_disposed)
                {
                    m_disposed = true;
                    if (m_invalidateStoreOnDispose)
                    {
                        InvalidateStore();
                    }

                    if (m_store is IDisposable disposableStore)
                    {
                        disposableStore.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Returns true if the instance is not in a valid state due to errors or because it was already disposed.
        /// Checking <see cref="m_disposed"/> here is NOT thread-safe, but because <see cref="Disabled"/> is checked frequently,
        /// for performance reasons it is checked without any locking mechanism.
        /// </summary>
        public bool Disabled => m_disabledFailure != null || m_disposed;

        private Failure DisposedOrDisabledFailure
        {
            get
            {
                Contract.Requires(Disabled);
                
                // First check the error
                if (m_disabledFailure != null)
                {
                    return m_disabledFailure;
                }

                return s_disposedFailure;
            }
        }
    }
}
