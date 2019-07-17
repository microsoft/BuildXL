// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Engine
{
    /// <summary>
    /// Helper to take a lock on a folder.
    /// </summary>
    internal sealed class FolderLock : IDisposable
    {
        private readonly FileStream m_folderLock;

        /// <summary>
        /// returns whether the lock was taken successfully
        /// </summary>
        public bool SuccessfullyCreatedLock => m_folderLock != null;

        /// <summary>
        /// Private constructor, please use the Take factory.
        /// </summary>
        private FolderLock(FileStream folderLock)
        {
            m_folderLock = folderLock;
        }

        /// <summary>
        /// Private constructor for when the lock could not be aquired, please use Take factory
        /// </summary>
        private FolderLock()
        {
            m_folderLock = null;
        }

        /// <summary>
        /// Returns the full path to the directory lock file
        /// </summary>
        public static string GetLockPath(string folderPath)
        {
            return Path.Combine(folderPath, ".lock");
        }

        /// <summary>
        /// Takes a file lock on the folder
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "Stream is held by lock who is responsible for disposing.")]
        public static FolderLock Take(LoggingContext loggingContext, string path, int retryDelayInSeconds, int totalWaitTimeMins)
        {
            Contract.Ensures(Contract.Result<FolderLock>().SuccessfullyCreatedLock || loggingContext.ErrorWasLogged);

            string lockPath = GetLockPath(path);
            try
            {
                Directory.CreateDirectory(path);

                // Open a file in each output directory with exclusive mode so as to prevent other instances of
                // BuildXL from trying to output to the same directory at the same time (which wouldn't be good)
                var folderLock = TakeInternal(loggingContext, lockPath, path, retryDelayInSeconds, totalWaitTimeMins);
                return new FolderLock(folderLock);
            }
            catch (IOException ex)
            {
                Tracing.Logger.Log.BusyOrUnavailableOutputDirectories(loggingContext, path, ex.ToString());
                return new FolderLock();
            }
            catch (UnauthorizedAccessException ex)
            {
                Tracing.Logger.Log.BusyOrUnavailableOutputDirectories(loggingContext, path, ex.ToString());
                return new FolderLock();
            }
        }

        private static FileStream TakeInternal(
            LoggingContext loggingContext,
            string lockPath,
            string directoryPath,
            int retryDelayInSeconds,
            int buildLockWaitTimeMins)
        {
            Stopwatch sw = Stopwatch.StartNew();
            Stopwatch totalWaitTime = Stopwatch.StartNew();
            bool secondBuild = false;

            // TimeSpan.FromSeconds and TimeSpan.FromMinutes can throw OverflowException.
            // First time always check for 5 seconds, to make sure this is actually a second build.
            // In a normal case of execution, a first build can experience few "not ready" states that
            // can cause exception, but it usually is done in no more than 0-2 attempts.
            // After this first attempt, it is a second build, so use the interval provided by the user.
            // This way we also preserve the current behavior.
            TimeSpan timeoutSpan = TimeSpan.FromSeconds(5);
            TimeSpan buildWaitTimeMins = TimeSpan.FromMinutes(buildLockWaitTimeMins);

            while (true)
            {
                try
                {
                    return new FileStream(lockPath, FileMode.Create, FileAccess.Write, FileShare.None, 8, FileOptions.DeleteOnClose);
                }
                catch (IOException)
                {
                    if (secondBuild || sw.Elapsed >= timeoutSpan)
                    {
                        secondBuild = true;

                        Tracing.Logger.Log.BusyOrUnavailableOutputDirectoriesRetry(loggingContext, buildLockWaitTimeMins, directoryPath);

                        if (totalWaitTime.Elapsed >= buildWaitTimeMins)
                        {
                            throw;
                        }

                        Thread.Sleep(retryDelayInSeconds * 1000);

                        continue;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Don't retry more than once. This is not a busy lock.
                    if (sw.Elapsed >= timeoutSpan)
                    {
                        throw;
                    }
                }

                Thread.Sleep(100);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            m_folderLock?.Dispose();
        }
    }
}
