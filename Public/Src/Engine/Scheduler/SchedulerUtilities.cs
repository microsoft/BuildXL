// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Utilities for <see cref="Scheduler"/>.
    /// </summary>
    public class SchedulerUtilities
    {
        /// <summary>
        /// Removes a corrupt file and attempts to save it to the appropriate logs directory for future debugging.
        /// WARNING: This should only be called if it is ok to delete the corrupt file.
        /// </summary>
        /// <param name="corruptFile">
        /// The string path of the corrupt file to copy.
        /// </param>
        /// <param name="configuration">
        /// The BuildXL configuration being used to determine logs layout.
        /// </param>
        /// <param name="pathTable">
        /// PathTable for expanding the BuildXL logs path.
        /// </param>
        /// <param name="loggingContext">
        /// LoggingContext for verbose and warning messages.
        /// </param>
        /// <param name="removeFile">
        /// Whether or not the corrupt file should be removed.
        /// Conservatively defaults to false.
        /// </param>
        public static bool TryLogAndMaybeRemoveCorruptFile(string corruptFile, IConfiguration configuration, PathTable pathTable, LoggingContext loggingContext, bool removeFile = false)
        {
            if (!FileUtilities.FileExistsNoFollow(corruptFile))
            {
                // If file does not exist, then this method should return true.
                // If file does not exist, move file or copy file below will throw an exception.
                // The exception is logged in the catch clause, but the log is a warning log.
                // Customer may treat that warning as an error.
                return true;
            }

            // Build the destination path
            var destFolder = configuration.Logging.EngineCacheCorruptFilesLogDirectory.ToString(pathTable);
            var possibleFileName = FileUtilities.GetFileName(corruptFile);
            var fileName = possibleFileName.Succeeded ? possibleFileName.Result : Path.GetFileName(corruptFile);

            var destination = Path.Combine(destFolder, fileName);

            Logger.Log.MovingCorruptFile(loggingContext, corruptFile, destination);

            try
            {
                FileUtilities.CreateDirectory(destFolder);

                if (removeFile)
                {
                    FileUtilities.MoveFileAsync(corruptFile, destination).GetAwaiter().GetResult();
                }
                else
                {
                    FileUtilities.CopyFileAsync(corruptFile, destination).GetAwaiter().GetResult();
                }

                return true;
            }
            catch (BuildXLException moveException)
            {
                Logger.Log.FailedToMoveCorruptFile(loggingContext, corruptFile, destination, moveException.ToStringDemystified());
            }

            // If moving the file to logs for debugging failed, still attempt to delete the file since it's corrupt
            if (removeFile)
            {
                try
                {
                    FileUtilities.DeleteFile(corruptFile, waitUntilDeletionFinished: true);
                }
                catch (Exception deleteException)
                {
                    Logger.Log.FailedToDeleteCorruptFile(loggingContext, corruptFile, deleteException.ToStringDemystified());
                }
            }

            return false;
        }
    }
}
