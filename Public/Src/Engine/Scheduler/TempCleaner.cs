// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Cleans temp directories in the background. Only deletes the contents of directories and, optionally, the directory itself.
    /// Leaving the directory itself not deleted may save work in the subsequent build.
    /// This is used for cleaning <see cref="BuildXL.Pips.Operations.Process.TempDirectory"/>,
    /// <see cref="BuildXL.Pips.Operations.Process.AdditionalTempDirectories"/>.
    /// It is also used as a <see cref="ITempDirectoryCleaner"/> implementation and cleans <see cref="TempCleaner.TempDirectory"/>.
    /// </summary>
    public sealed class TempCleaner : ITempDirectoryCleaner
    {
        /// <summary>
        /// Simple holder that stores path information.
        /// </summary>
        private readonly struct FileOrDirectoryWrapper
        {
            public readonly string Path;
            public readonly bool IsFile;
            public readonly bool DeleteRootDirectory;

            private FileOrDirectoryWrapper(string path, bool isFile, bool deleteRootDirectory)
            {
                Path = path;
                IsFile = isFile;
                DeleteRootDirectory = deleteRootDirectory;
            }

            public static FileOrDirectoryWrapper CreateDirectory(string path, bool deleteRootDirectory)
            {
                return new FileOrDirectoryWrapper(path, isFile: false, deleteRootDirectory: deleteRootDirectory);
            }

            public static FileOrDirectoryWrapper CreateFile(string path)
            {
                return new FileOrDirectoryWrapper(path, isFile: true, deleteRootDirectory: false);
            }
        }

        private long m_directoriesFailedToClean;
        private long m_directoriesCleaned;
        private long m_directoriesPending;

        private long m_filesFailedToClean;
        private long m_filesCleaned;
        private long m_filesPending;

        private readonly Thread m_cleanerThread;
        private bool m_disposed;

        private readonly BlockingCollection<FileOrDirectoryWrapper> m_pathsToDelete = new BlockingCollection<FileOrDirectoryWrapper>();
        private readonly CancellationTokenSource m_cancellationSource = new CancellationTokenSource();

        /// <inheritdoc />
        /// <remarks>
        /// This temp directory contained in <see cref="TempCleaner"/> is used by <see cref="BuildXL.Native.IO.FileUtilities"/> for
        /// move-deleting files. <see cref="TempCleaner"/> is responsible for cleaning the temp directory.
        /// </remarks>
        public string TempDirectory { get; private set; }

        /// <summary>
        /// Creates a <see cref="TempCleaner"/> instance.
        /// </summary>
        public TempCleaner(string tempDirectory = null)
        {
            TempDirectory = tempDirectory;
            if (TempDirectory != null)
            {
                Contract.Requires(FileUtilities.Exists(tempDirectory));

                // Register temp directory used for move-deleting files for cleaning
                RegisterDirectoryToDelete(tempDirectory, deleteRootDirectory: false /* BuildXL locks tempDirectory */);
            }

            m_cleanerThread = new Thread(CleanPaths)
                              {
                                  Name = nameof(TempCleaner),
                                  IsBackground = true,
                                  Priority = ThreadPriority.BelowNormal,
                              };

            m_cleanerThread.Start();
        }

        /// <summary>
        /// Registers a directory to delete.
        /// </summary>
        /// <remarks>
        /// The contents of registered directory will be deleted. The directory itself will be deleted
        /// if <paramref name="deleteRootDirectory"/> is true.
        /// </remarks>
        public void RegisterDirectoryToDelete(string path, bool deleteRootDirectory)
        {
            ThrowIfDisposed();

            m_pathsToDelete.Add(FileOrDirectoryWrapper.CreateDirectory(path, deleteRootDirectory));
            Interlocked.Increment(ref m_directoriesPending);
        }

        /// <summary>
        /// Registers a file to delete.
        /// </summary>
        public void RegisterFileToDelete(string path)
        {
            ThrowIfDisposed();

            m_pathsToDelete.Add(FileOrDirectoryWrapper.CreateFile(path));
            Interlocked.Increment(ref m_filesPending);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!m_disposed)
            {
                m_cancellationSource.Cancel();
                WaitPendingTasksForCompletion();

                Tracing.Logger.Log.PipTempCleanerSummary(
                    Events.StaticContext,
                    SucceededDirectories,
                    FailedDirectories,
                    PendingDirectories,
                    SucceededFiles,
                    FailedFiles,
                    PendingFiles);

                m_pathsToDelete.Dispose();
                m_cancellationSource.Dispose();

                m_disposed = true;
            }
        }

        /// <summary>
        /// Internal method that will wait till all the pending tasks are running
        /// </summary>
        internal void WaitPendingTasksForCompletion()
        {
            ThrowIfDisposed();

            // In this case all pending items would be processed before thread method would finish execution.
            m_pathsToDelete.CompleteAdding();
            m_cleanerThread.Join();
        }

        internal long SucceededDirectories => Interlocked.Read(ref m_directoriesCleaned);

        internal long FailedDirectories => Interlocked.Read(ref m_directoriesFailedToClean);

        internal long PendingDirectories => Interlocked.Read(ref m_directoriesPending);

        internal long SucceededFiles => Interlocked.Read(ref m_filesCleaned);

        internal long FailedFiles => Interlocked.Read(ref m_filesFailedToClean);

        internal long PendingFiles => Interlocked.Read(ref m_filesPending);

        /// <summary>
        /// Performs the work of cleaning files and directories
        /// </summary>
        private void CleanPaths()
        {
            try
            {
                foreach (var toClean in m_pathsToDelete.GetConsumingEnumerable(m_cancellationSource.Token))
                {
                    if (toClean.IsFile)
                    {
                        TryDeleteFile(toClean.Path);
                    }
                    else
                    {
                        TryDeleteDirectory(toClean.Path, toClean.DeleteRootDirectory, m_cancellationSource.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Let the thread exit once canceled
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(TempCleaner));
            }
        }

        private void TryDeleteDirectory(string path, bool deleteRootDirectory, CancellationToken cancellationToken)
        {
            Interlocked.Decrement(ref m_directoriesPending);

            try
            {
                if (FileUtilities.DirectoryExistsNoFollow(path))
                {
                    FileUtilities.DeleteDirectoryContents(path, deleteRootDirectory: deleteRootDirectory, tempDirectoryCleaner: this, cancellationToken: cancellationToken);
                    Interlocked.Increment(ref m_directoriesCleaned);
                }
            }
            catch (Exception ex) when (ex is BuildXLException || ex is OperationCanceledException)
            {
                // Temp directory cleanup is best effort. Keep track of failures for the sake of reporting
                Tracing.Logger.Log.PipFailedTempDirectoryCleanup(Events.StaticContext, path, ex.Message);
                Interlocked.Increment(ref m_directoriesFailedToClean);
            }
        }

        private void TryDeleteFile(string path)
        {
            Interlocked.Decrement(ref m_filesPending);

            try
            {
                FileUtilities.DeleteFile(path, tempDirectoryCleaner: this);
                Interlocked.Increment(ref m_filesCleaned);
            }
            catch (BuildXLException ex)
            {
                Tracing.Logger.Log.PipFailedTempFileCleanup(Events.StaticContext, path, ex.LogEventMessage);
                Interlocked.Increment(ref m_filesFailedToClean);
            }
        }
    }
}
