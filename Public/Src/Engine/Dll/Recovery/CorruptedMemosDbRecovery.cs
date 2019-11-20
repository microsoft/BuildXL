// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tasks;
using JetBrains.Annotations;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine.Recovery
{
    /// <summary>
    /// Action for failure caused by corrupted memos.db (SQLite database).
    /// </summary>
    /// <remarks>
    /// This recovery only works if the cache configuration (JSON file) has 'CacheRootPath' entry
    /// that specifies the root of the cache. Currently, this entry only exists in MemoizationStoreAdapterCacheFactory.
    /// </remarks>
    internal class CorruptedMemosDbRecovery : FailureRecovery
    {
        private const string MemosDbFileName = "Memos.db";
        private const string CorruptedMemosDbPrefix = "CorruptedMemosDb";
        private const string CorruptedMemosDbMarkerFileName = "CorruptedMemosDbMarker";
        private const string CacheRootPathFieldInConfig = "CacheRootPath";
        private const int MaxCorruptedMemos = 5;
        private readonly Possible<string> m_mayBeCacheRoot;

        /// <summary>
        /// Creates an instance of <see cref="CorruptedMemosDbRecovery"/>.
        /// </summary>
        public CorruptedMemosDbRecovery(PathTable pathTable, IConfiguration configuration)
            : base(nameof(CorruptedMemosDbRecovery), pathTable, configuration)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(pathTable.IsValid);
            Contract.Requires(configuration != null);

            m_mayBeCacheRoot = TryGetCacheRootDirectory();
        }

        /// <inheritdoc />
        public override bool ShouldRecover()
        {
            if (!m_mayBeCacheRoot.Succeeded)
            {
                return false;
            }

            return File.Exists(MarkerFile(m_mayBeCacheRoot.Result));
        }

        /// <inheritdoc />
        public override bool ShouldMarkFailure([NotNull] Exception exception, ExceptionRootCause rootCause)
        {
            return rootCause == ExceptionRootCause.CorruptedCache;
        }

        /// <inheritdoc />
        public override Possible<Unit> MarkFailure([NotNull] Exception exception)
        {
            if (!m_mayBeCacheRoot.Succeeded)
            {
                return m_mayBeCacheRoot.Failure;
            }

            string markerFile = MarkerFile(m_mayBeCacheRoot.Result);

            try
            {
                // Marker is just an empty file.
                File.WriteAllText(markerFile, string.Empty);
                return Unit.Void;
            }
            catch (Exception e)
            {
                return new Failure<string>(I($"Unable to mark failure at '{markerFile}': {e.GetLogEventMessage()}"));
            }
        }

        /// <summary>
        /// Tries to recover from possible corrupted memos.db (SQLite) by renaming the current memos.db for a backup.
        /// </summary>
        public override Possible<Unit> Recover()
        {
            if (!m_mayBeCacheRoot.Succeeded)
            {
                return m_mayBeCacheRoot.Failure;
            }

            string cacheDirectory = m_mayBeCacheRoot.Result;
            string memosDbFile = MemosDbFile(cacheDirectory);

            if (!File.Exists(memosDbFile))
            {
                // Nothing to recover.
                return Unit.Void;
            }

            // Ideally this should run the cache integrity check first, as follows:
            //
            //     var cacheIntegrityResult = Cache.RunIntegrityCheck();
            //     if (cacheIntegrityResult == Success) return;
            //     else {
            //         Assert(cacheIntegrityResult == NoIssueFound);
            //         RenameMemoDb();
            //     }
            try
            {
                var corruptedMemosDbFile = PrepareCorruptedMemosDbBackupFile(cacheDirectory);
                FileUtilities.MoveFileAsync(memosDbFile, corruptedMemosDbFile, replaceExisting: true).Wait();

                // Delete marker file in case of successful recovery.
                FileUtilities.DeleteFile(MarkerFile(cacheDirectory));
            }
            catch (BuildXLException ex)
            {
                return new RecoverableExceptionFailure(ex);
            }

            return Unit.Void;
        }

        private string PrepareCorruptedMemosDbBackupFile(string cacheDirectory)
        {
            var existingCorruptedMemos = GetAllCorruptedMemosDb().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

            if (existingCorruptedMemos.Length >= MaxCorruptedMemos)
            {
                for (int i = 0; i < (existingCorruptedMemos.Length - MaxCorruptedMemos) + 1; ++i)
                {
                    FileUtilities.DeleteFile(existingCorruptedMemos[i]);
                }
            }

            string dateBasedName = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            dateBasedName = I($"{CorruptedMemosDbPrefix}.{dateBasedName}");
            string uniqueName = dateBasedName;

            int idx = 1;
            while (File.Exists(Path.Combine(cacheDirectory, uniqueName)))
            {
                uniqueName = I($"{dateBasedName}.{idx}");
                ++idx;
            }

            return Path.Combine(cacheDirectory, uniqueName);
        }

        internal IEnumerable<string> GetAllCorruptedMemosDb()
        {
            if (!m_mayBeCacheRoot.Succeeded)
            {
                return Enumerable.Empty<string>();
            }

            return Directory.EnumerateFiles(m_mayBeCacheRoot.Result, I($"{CorruptedMemosDbPrefix}.*"));
        }

        private static string MemosDbFile(string cacheDirectory) => Path.Combine(cacheDirectory, MemosDbFileName);

        private static string MarkerFile(string cacheDirectory) => Path.Combine(cacheDirectory, CorruptedMemosDbMarkerFileName);

        private Possible<string> TryGetCacheRootDirectory()
        {
            var cacheConfigData = CacheCoreCacheInitializer.TryGetCacheConfigData(
                PathTable,
                Configuration.Layout.CacheDirectory.ToString(PathTable),
                Configuration.Cache);

            if (!cacheConfigData.Succeeded)
            {
                return cacheConfigData.Failure;
            }

            object rootPath;
            if (!cacheConfigData.Result.TryGetValue(CacheRootPathFieldInConfig, out rootPath) || (rootPath as string) == null)
            {
                return new Failure<string>(I($"{CacheRootPathFieldInConfig} is not specified in the cache configuration"));
            }

            return rootPath as string;
        }
    }
}
