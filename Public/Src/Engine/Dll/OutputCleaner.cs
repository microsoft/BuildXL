// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Engine
{
    /// <summary>
    /// Cleans build outputs independently of executing pips
    /// </summary>
    public static class OutputCleaner
    {
        private const string Category = "OutputCleaner";

        /// <summary>
        /// Cleans output files and directories.
        /// </summary>
        public static bool DeleteOutputs(
            LoggingContext loggingContext,
            Func<DirectoryArtifact, bool> isOutputDir,
            IList<FileOrDirectoryArtifact> filesOrDirectoriesToDelete,
            PathTable pathTable,
            ITempCleaner tempDirectoryCleaner = null)
        {
            int fileFailCount = 0;
            int fileSuccessCount = 0;
            int directoryFailCount = 0;
            int directorySuccessCount = 0;

            using (PerformanceMeasurement.Start(
                loggingContext,
                Category,
                Tracing.Logger.Log.CleaningStarted,
                localLoggingContext =>
                {
                    Tracing.Logger.Log.CleaningFinished(loggingContext, fileSuccessCount, fileFailCount);
                    LoggingHelpers.LogCategorizedStatistic(loggingContext, Category, "FilesDeleted", fileSuccessCount);
                    LoggingHelpers.LogCategorizedStatistic(loggingContext, Category, "FilesFailed", fileFailCount);
                    LoggingHelpers.LogCategorizedStatistic(loggingContext, Category, "DirectoriesDeleted", directorySuccessCount);
                    LoggingHelpers.LogCategorizedStatistic(loggingContext, Category, "DirectoriesFailed", directoryFailCount);
                }))
            {
                // Note: filesOrDirectoriesToDelete better be an IList<...> in order to get good Parallel.ForEach performance
                Parallel.ForEach(
                    filesOrDirectoriesToDelete,
                    fileOrDirectory =>
                    {
                        string path = fileOrDirectory.Path.ToString(pathTable);
                        Tracing.Logger.Log.CleaningOutputFile(loggingContext, path);

                        try
                        {
                            if (fileOrDirectory.IsFile)
                            {
                                Contract.Assume(fileOrDirectory.FileArtifact.IsOutputFile, "Encountered non-output file");
                                if (FileUtilities.FileExistsNoFollow(path))
                                {
                                    FileUtilities.DeleteFile(path, waitUntilDeletionFinished: true, tempDirectoryCleaner: tempDirectoryCleaner);
                                    Interlocked.Increment(ref fileSuccessCount);
                                }
                            }
                            else
                            {
                                if (FileUtilities.DirectoryExistsNoFollow(path))
                                {
                                    // TODO:1011977 this is a hacky fix for a bug where we delete SourceSealDirectories in /cleanonly mode
                                    // The bug stems from the fact that FilterOutputs() returns SourceSealDirectories, which aren't inputs.
                                    // Once properly addressed, this check should remain in here as a safety precaution and turn into
                                    // a Contract.Assume() like the check above
                                    if (isOutputDir(fileOrDirectory.DirectoryArtifact))
                                    {
                                        FileUtilities.DeleteDirectoryContents(path, deleteRootDirectory: false, tempDirectoryCleaner: tempDirectoryCleaner);
                                        Interlocked.Increment(ref directorySuccessCount);
                                    }
                                }
                            }
                        }
                        catch (BuildXLException ex)
                        {
                            if (fileOrDirectory.IsFile)
                            {
                                Interlocked.Increment(ref fileFailCount);
                                Tracing.Logger.Log.CleaningFileFailed(loggingContext, path, ex.LogEventMessage);
                            }
                            else
                            {
                                Interlocked.Increment(ref directoryFailCount);
                                Tracing.Logger.Log.CleaningDirectoryFailed(loggingContext, path, ex.LogEventMessage);
                            }
                        }
                    });
            }

            return fileFailCount + directoryFailCount == 0;
        }
    }
}
