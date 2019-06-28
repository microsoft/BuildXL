// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.Streams.Windows;
using BuildXL.Native.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Native.IO.Windows
{
    /// <inheritdoc />
    public sealed class FileUtilitiesWin : IFileUtilities
    {
        /// <summary>
        /// A concrete native FileSystem implementation based on Windows APIs
        /// </summary>
        private static readonly FileSystemWin s_fileSystem = new FileSystemWin();

        private static readonly SecurityIdentifier s_worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

        /// <inheritdoc />
        public PosixDeleteMode PosixDeleteMode { get; set; }

        /// <summary>
        /// Creates a concrete FileUtilities instance
        /// </summary>
        public FileUtilitiesWin()
        {
            PosixDeleteMode = PosixDeleteMode.RunLast;
        }

        /// <summary>
        /// Canonicalizes path by calling GetFullPathNameW.
        /// </summary>
        /// <remarks>
        /// Marked internal rather than private for testing purposes
        /// </remarks>
        public static string NormalizeDirectoryPath(string path)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));

            string fullPath = s_fileSystem.GetFullPath(path, out int hr);

            if (fullPath == null)
            {
                throw new BuildXLException(I($"Failed to normalize directory '{path}'"), new NativeWin32Exception(hr));
            }

            return fullPath.TrimEnd(Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Checks whether a character may represent a drive letter
        /// </summary>
        [Pure]
        public static bool IsDriveLetter(char driveLetter) => char.IsLetter(driveLetter) && driveLetter > 64 && driveLetter < 123;

        /// <inheritdoc />
        public bool? DoesLogicalDriveHaveSeekPenalty(char driveLetter)
        {
            Contract.Requires(IsDriveLetter(driveLetter), "Drive letter must be an ASCII letter");

            bool? result = default(bool?);

            ExceptionUtilities.HandleRecoverableIOException(
                () =>
                {
                    string path = FileSystemWin.LocalDevicePrefix + driveLetter + ":";
                    SafeFileHandle handle;
                    var handleResult = s_fileSystem.TryCreateOrOpenFile(
                        path,
                        FileDesiredAccess.None,
                        FileShare.Read,
                        FileMode.Open,
                        FileFlagsAndAttributes.FileAttributeNormal,
                        out handle);

                    using (handle)
                    {
                        if (handleResult.Succeeded && handle != null && !handle.IsInvalid)
                        {
                            bool hasSeekPenalty;
                            int error;
                            if (s_fileSystem.TryReadSeekPenaltyProperty(handle, out hasSeekPenalty, out error))
                            {
                                result = hasSeekPenalty;
                            }
                        }
                    }
                },
                ex => { throw new BuildXLException(I($"Failed to query for seek penalty. Path: {driveLetter}"), ex); });

            return result;
        }

        /// <inheritdoc />
        public void DeleteDirectoryContents(
            string path,
            bool deleteRootDirectory = false,
            Func<string, bool> shouldDelete = null,
            ITempDirectoryCleaner tempDirectoryCleaner = null,
            CancellationToken? cancellationToken = default)
        {
            var maybeExistence = s_fileSystem.TryProbePathExistence(path, followSymlink: true);

            if (!maybeExistence.Succeeded || maybeExistence.Result != PathExistence.ExistsAsDirectory)
            {
                return;
            }

            DeleteDirectoryAndContentsInternal(
                NormalizeDirectoryPath(path),
                deleteRootDirectory: deleteRootDirectory,
                shouldDelete: shouldDelete,
                tempDirectoryCleaner: tempDirectoryCleaner,
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Deletes directory and its contents
        /// </summary>
        /// <remarks>
        /// This may fail if another process changes the state of the directory while it is being deleted.
        /// Note that if <paramref name="deleteRootDirectory"/> is set, the directory at the given path may be present in the 'pending deletion' state
        /// for a short time. Otherwise, the directory is guaranteed truly empty on successful return (no children pending deletion).
        /// Supports paths beyond MAX_PATH, no prefix is needed. Does not support UNC paths
        /// </remarks>
        /// <exception cref="IOException">Thrown when operation fails</exception>
        /// <param name="directoryPath">Absolute path to the directory to delete. It is assumed that this is canonicalized.</param>
        /// <param name="deleteRootDirectory">If false, only the contents of the root directory will be deleted</param>
        /// <param name="shouldDelete">a function which returns true if file should be deleted and false otherwise.</param>
        /// <param name="tempDirectoryCleaner">provides and cleans a temp directory for move-deletes</param>
        /// <param name="cancellationToken">provides cancellation capability</param>
        /// <returns>
        /// How many entries remain in the directory, the count is not recursive. This function could be successful with a count greater than one because of <paramref name="shouldDelete"/>
        /// </returns>
        /// <exception cref="BuildXLException">Throws BuildXLException</exception>
        private int DeleteDirectoryAndContentsInternal(
            string directoryPath,
            bool deleteRootDirectory,
            Func<string, bool> shouldDelete = null,
            ITempDirectoryCleaner tempDirectoryCleaner = null,
            CancellationToken? cancellationToken = default)
        {
            var defaultDeleteCheck = new Func<string, bool>(p => true);
            shouldDelete = shouldDelete ?? defaultDeleteCheck;
            int remainingChildCount = 0;
            using (var stringSetPool = Pools.GetStringSet())
            {
                // Maintain a list of files that that were enumerated for deletion
                HashSet<string> pathsEnumeratedForDeletion = stringSetPool.Instance;

                // Enumerate the directory, deleting contents along the way
                // The enumeration operation itself is not recursive, but there is a recursive call to DeleteDirectoryAndContentsInternal
                EnumerateDirectoryResult result = s_fileSystem.EnumerateDirectoryEntries(
                    directoryPath,
                    (name, attr) =>
                    {
                        cancellationToken?.ThrowIfCancellationRequested();

                        string childPath = Path.Combine(directoryPath, name);
                        if ((attr & FileAttributes.Directory) != 0)
                        {
                            int subDirectoryEntryCount = DeleteDirectoryAndContentsInternal(
                                childPath,
                                deleteRootDirectory: true,
                                shouldDelete: shouldDelete,
                                tempDirectoryCleaner: tempDirectoryCleaner,
                                cancellationToken: cancellationToken);
                            if (subDirectoryEntryCount > 0)
                            {
                                remainingChildCount++;
                            }
                        }
                        else
                        {
                            if (shouldDelete(childPath))
                            {
                                // This method already has retry logic, so no need to do retry in DeleteFile
                                DeleteFile(childPath, waitUntilDeletionFinished: true, tempDirectoryCleaner: tempDirectoryCleaner);
                            }
                            else
                            {
                                remainingChildCount++;
                            }
                        }

                        // This is not a recursive list
                        pathsEnumeratedForDeletion.Add(childPath);
                    }, isEnumerationForDirectoryDeletion: true);

                // If result.Status == EnumerateDirectoryStatus.SearchDirectoryNotFound that means the directory is already deleted.
                // This could happen if this is a reparse point directory and the target got deleted first.
                if (!result.Succeeded && result.Status != EnumerateDirectoryStatus.SearchDirectoryNotFound)
                {
                    throw new BuildXLException(
                        I($"Deleting directory contents failed; unable to enumerate the directory or a descendant directory. Directory: {directoryPath}"),
                        result.CreateExceptionForError());
                }

                // When attempting to delete directory contents, there may be times when we successfully clear the contents of a directory, then try to delete the empty directory
                // and still receive ERROR_DIR_NOT_EMPTY. In this case, we back off and retry.
                //
                // The error occcurs if some handle A is opened to the file with FileShare.Delete mode specified, and we open handle B also and successfully delete the file.
                // Windows will not actually delete the file until both handles A and B (and any other handles) are closed. Until then, the file
                // is placed on the Windows delete queue and marked as pending deletion. Any attempt to access the file after it is pending deletion will cause either
                // [Windows NT Status Code STATUS_DELETE_PENDING] or [WIN32 Error code ERROR_ACCESS_DENIED] depending on the API.
                // Note: This can only occur if all the open handles remaining were opened with FileShare.Delete, otherwise we would not have been able to successfully
                // delete the file in the first place.
                //
                // Case 1: If we are deleting the root directory, the caller needs to worry about that same problem (recursively, things work out, so long as the outermost directory is emptied with deleteRootDirectory==false; see next case).
                // Case 2: Otherwise, we poll the root directory until it is empty (so the caller can be guaranteed it is *really* empty w.r.t. re-creating files or directories with existing names)
                bool success = false;
                if (deleteRootDirectory && remainingChildCount == 0)
                {
                    success = Helpers.RetryOnFailure(
                        finalRound =>
                        {
                            // An exception will be thrown on failure, which will trigger a retry
                            DeleteEmptyDirectory(directoryPath, tempDirectoryCleaner: tempDirectoryCleaner, logFailures: finalRound);
                            // Only reached if there are no exceptions
                            return true;
                        });
                }
                else
                {
                    // Verify the directory has the correct number of entries. Attempt retries in case there is a pending child-delete that may complete.
                    success = Helpers.RetryOnFailure(
                        work: (finalRound) =>
                        {
                            int actualRemainingChildCount = 0;

                            // Poll the root directory (we don't want to delete it): We want to guarantee there are no lingering (pending-delete) children before returning (see above).
                            result = s_fileSystem.EnumerateDirectoryEntries(
                                directoryPath,
                                (name, attr) => { actualRemainingChildCount++; });

                            if (!result.Succeeded)
                            {
                                throw new BuildXLException(
                                    I($"Deleting directory contents failed; unable to enumerate the directory to verify that it has been emptied. Directory: {directoryPath}"),
                                    result.CreateExceptionForError());
                            }

                            if (actualRemainingChildCount < remainingChildCount)
                            {
                                // Another process/operation
                                throw new BuildXLException(I($"Deleting directory contents failed; Directory entry deleted which should have been retained. Directory: {directoryPath}"));
                            }

                            // This function does not prevent concurrent writes to the directory during deletion
                            // In that case, there is a race condition between enumerating the directory for deletion and enumerating the directory to verify the count of remaining children
                            // If the race condition occurs (actualRemainingChildCount > remainingChildCount) and a shouldDelete func was provided,
                            // optimistically (and for efficiency) assume the new items would not have been deleted in the first place and return true
                            if (!ReferenceEquals(shouldDelete, defaultDeleteCheck))
                            {
                                // Attempt to fix the count for caller's up the recursion stack
                                remainingChildCount = actualRemainingChildCount;
                                return true;
                            }

                            // Deletion is successful when the child directories/files count matches the expected count
                            return actualRemainingChildCount == remainingChildCount;
                        },
                        rethrowExceptions: true /* exceptions thrown in work() will occur again on retry, so just rethrow them */);
                }

                if (!success)
                {
                    // Send a signal to eventcount telemetry
                    Logger.Log.FileUtilitiesDirectoryDeleteFailed(Events.StaticContext, directoryPath);

                    // We failed to delete the directory after everything. Enumerate the contents of the directory
                    // for sake of debugging.
                    // Note: deletedPaths is not a recursive list, so it does not contain nested paths. However, because of
                    // recursion, we can assume that any directories in deletedPaths were successfully emptied. Otherwise,
                    // we would have thrown an exception for those already.
                    throw new BuildXLException(FileUtilitiesMessages.DeleteDirectoryContentsFailed + directoryPath +
                        " Directory contents: " + Environment.NewLine + FindAllOpenHandlesInDirectory(directoryPath, pathsEnumeratedForDeletion));
                }

                return remainingChildCount;
            }
        }

        /// <summary>
        /// Tiered attempt to delete an empty directory, ordered by likelihood of success. Returns if sucessful,
        /// otherwise throws an exception.
        /// </summary>
        /// <param name="directoryPath">The empty directory to delete</param>
        /// <param name="tempDirectoryCleaner">provides and cleans a temp directory for move-deletes</param>
        private void DeleteEmptyDirectoryHelper(string directoryPath, ITempDirectoryCleaner tempDirectoryCleaner)
        {
            // Attempt 1: POSIX delete
            if (PosixDeleteMode == PosixDeleteMode.RunFirst && RunPosixDelete(directoryPath, out _))
            {
                return;
            }

            // Attempt 2: move-delete
            string deletionTempDirectory = tempDirectoryCleaner?.TempDirectory;
            if (!string.IsNullOrWhiteSpace(deletionTempDirectory) && !directoryPath.Contains(deletionTempDirectory))
            {
                if (TryMoveDelete(directoryPath, deletionTempDirectory))
                {
                    return;
                }
            }

            // Attempt 3: Windows delete, this may leave the directory in a pending delete state that causes Access Denied errors,
            // so it is attempted last
            s_fileSystem.RemoveDirectory(directoryPath);

            // Attempt 4: If directory still exists, then try run POSIX delete if enabled. 
            var possiblyPathExistence = FileUtilities.TryProbePathExistence(directoryPath, false);

            if (possiblyPathExistence.Succeeded 
                && possiblyPathExistence.Result == PathExistence.ExistsAsDirectory
                && PosixDeleteMode == PosixDeleteMode.RunLast)
            {
                RunPosixDelete(directoryPath, out _);
            }
        }

        /// <summary>
        /// Attempts to delete an empty directory. The caller is responsible for verifying the directory is empty beforehand.
        /// Similar to <see cref="Directory.Delete(string)"/>, if any error occurs, an exception is thrown. Otherwise, the delete is successful.
        /// </summary>
        /// <param name="directoryPath">The empty directory to delete</param>
        /// <param name="tempDirectoryCleaner">provides and cleans a temp directory for move-deletes</param>
        /// <param name="logFailures">Whether information about failures should be logged</param>
        private void DeleteEmptyDirectory(string directoryPath, ITempDirectoryCleaner tempDirectoryCleaner, bool logFailures = true)
        {
            try
            {
                DeleteEmptyDirectoryHelper(directoryPath, tempDirectoryCleaner);
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == NativeIOConstants.ErrorAccessDenied)
                {
                    // Try taking ownership of the directory. If this fails, don't bother handling the failure
                    if (TryTakeOwnershipAndSetAcl(directoryPath))
                    {
                        try
                        {
                            DeleteEmptyDirectoryHelper(directoryPath, tempDirectoryCleaner);
                        }
                        catch (NativeWin32Exception secondaryDeleteException)
                        {
                            // The nested removal after setting the ACL may still fail after setting the directory ACL.
                            // If it does, fall back into the same handling pattern that would have been in effect
                            // on the initial delete.
                            ex = secondaryDeleteException;

                            if (logFailures)
                            {
                                Logger.Log.FileUtilitiesDiagnostic(
                                    Events.StaticContext,
                                    directoryPath,
                                    "Directory deletion failed after taking ownership of file with native error code: " +
                                    secondaryDeleteException.NativeErrorCode);
                            }
                        }
                    }
                    else
                    {
                        if (logFailures)
                        {
                            var reason = GetACL(directoryPath) ?? "Failed to get directory ACL from ICACLS";
                            Logger.Log.FileUtilitiesDiagnostic(
                                Events.StaticContext,
                                directoryPath,
                                I($"Failed to take ownership of the directory. Directory ACL: {reason}"));
                        }
                    }
                }

                if (ex.NativeErrorCode != NativeIOConstants.ErrorFileNotFound)
                {
                    // From observation, RemoveDirectory for a truly-empty directory is not quite synchronous enough: sometimes a subsequent creation attempt at that
                    // path will give STATUS_DELETE_PENDING (-> ERROR_ACCESS_DENIED) despite the fact that RemoveDirectory has already returned.
                    // In this case, we still conservatively rethrow the exception.
                    throw;
                }
            }
        }

        /// <inheritdoc />
        public string FindAllOpenHandlesInDirectory(string directoryPath, HashSet<string> pathsPossiblyPendingDelete = null)
        {
            pathsPossiblyPendingDelete = pathsPossiblyPendingDelete ?? new HashSet<string>();
            using (var builderPool = Pools.GetStringBuilder())
            {
                StringBuilder builder = builderPool.Instance;
                s_fileSystem.EnumerateDirectoryEntries(
                    directoryPath,
                    true,
                    (directoryName, filePath, attributes) =>
                    {
                        string fullPath = directoryName + Path.DirectorySeparatorChar + filePath;
                        builder.AppendLine(fullPath);

                        if (TryFindOpenHandlesToFile(fullPath, out var diagnosticInfo))
                        {
                            builder.AppendLine(diagnosticInfo);
                        }

                        // Since we cannot find open handles to deleted files, append a message for possibly pending deletes
                        if (pathsPossiblyPendingDelete.Contains(fullPath))
                        {
                            builder.AppendLine(FileUtilitiesMessages.PathMayBePendingDeletion);
                        }
                    });

                return builder.ToString();
            }
        }

        /// <inheritdoc />
        public Possible<Unit, RecoverableExceptionFailure> TryDeleteFile(string path, bool waitUntilDeletionFinished = true, ITempDirectoryCleaner tempDirectoryCleaner = null)
        {
            try
            {
                DeleteFile(path, waitUntilDeletionFinished, tempDirectoryCleaner);
                return Unit.Void;
            }
            catch (BuildXLException ex)
            {
                return new RecoverableExceptionFailure(ex);
            }
        }

        /// <inheritdoc />
        public bool Exists(string path)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));
            var maybeExistence = s_fileSystem.TryProbePathExistence(path, followSymlink: true);
            return maybeExistence.Succeeded && maybeExistence.Result != PathExistence.Nonexistent;
        }

        /// <inheritdoc />
        public void DeleteFile(string path, bool waitUntilDeletionFinished = true, ITempDirectoryCleaner tempDirectoryCleaner = null)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));

            OpenFileResult deleteResult = default(OpenFileResult);
            bool successfullyDeletedFile = false;

            if (waitUntilDeletionFinished)
            {
                successfullyDeletedFile = Helpers.RetryOnFailure(
                    lastRound => DeleteFileInternal(path, tempDirectoryCleaner, out deleteResult));
            }
            else
            {
                successfullyDeletedFile = DeleteFileInternal(path, tempDirectoryCleaner, out deleteResult);
            }

            if (!successfullyDeletedFile)
            {
                // This may happen if RetryOnFailure wasn't able to wait long enough (currently it is 100 ms at most)
                // to check that the file was actually deleted.
                // IMPORTANT: Do not move or delete this branch:  deleteResult.CreateExceptionForError() below expects error result and
                // successful result will cause a contract violation.
                if (deleteResult.Succeeded)
                {
                    throw new BuildXLException(I($"Deleting file {path} failed: file deletion taking too long."));
                }

                // TODO: Note that the inner exception here doesn't account for TryDeleteViaMoveReplacement;
                // that is mostly adequate since we only try it when we have an 'access denied' status, which
                // is a reasonable thing to report. But maybe that's misleading if temporary file creation failed.
                string diagnosticInfo = string.Empty;
                if (deleteResult.Status == OpenFileStatus.AccessDenied || deleteResult.Status == OpenFileStatus.SharingViolation)
                {
                    if (TryFindOpenHandlesToFile(path, out diagnosticInfo))
                    {
                        diagnosticInfo = ": " + diagnosticInfo;
                    }
                }

                throw new BuildXLException(FileUtilitiesMessages.FileDeleteFailed + path + diagnosticInfo, deleteResult.CreateExceptionForError());
            }
        }

        /// <summary>
        /// Internal logic that tries to delete a file
        /// </summary>
        /// <param name="path">Path of the file to delete. Supports paths greater than MAX_PATH</param>
        /// <param name="tempDirectoryCleaner">provides and cleans a temp directory for move-deletes</param>
        /// <param name="deleteResult">The OpenFileResult from opening the file to be deleted. This can provide
        /// additional information such as whether the file was already found to not exist when being opened. This only
        /// reflects information from the traditional open for delete operation. It does not apply to the attempt to
        /// delete the file via renaming.</param>
        /// <returns>True if the file was deleted. Note that deletes don't always happen synchronously, so a result of
        /// true doesn't necessarily mean the file is immediately gone. But it does give a high level of certainty that
        /// the file will be gone in the very near term future. False is returned if the delete failed but might succeed
        /// if retried again in the future.</returns>
        /// <exception cref="BuildXLException">Thrown when setting file attributes fails for an unknown cause and retries
        /// won't help.</exception>
        private bool DeleteFileInternal(string path, ITempDirectoryCleaner tempDirectoryCleaner, out OpenFileResult deleteResult)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));
            LoggingContext context = Events.StaticContext;

            if (PosixDeleteMode == PosixDeleteMode.RunFirst && RunPosixDelete(path, out deleteResult))
            {
                return true;
            }

            // Maybe we delete right away, or maybe the file wasn't there to begin with.
            // Note that we allow PathNotFound (directory missing), consistently with File.Delete.
            // The postcondition here is 'the file doesn't exist'.
            deleteResult = TryOpenForDeletion(path);
            if (deleteResult.Succeeded)
            {
                return true;
            }

            // Check for deleting absent file on first attempt only
            // This may be first attempt for OSes that don't support Posix delete
            if (deleteResult.Status == OpenFileStatus.FileNotFound)
            {
                return true;
            }

            // Maybe this failure was due to the read-only flag.
            if (deleteResult.Status == OpenFileStatus.AccessDenied)
            {
                // Before attempting new method, log failure of previous attempt
                Logger.Log.FileUtilitiesDiagnostic(
                    context,
                    path,
                    I($"Open for deletion failed with native error code: {deleteResult.NativeErrorCode}. Attempting to remove readonly attribute."));
                ExceptionUtilities.HandleRecoverableIOException(
                    () =>
                    {
                        // Check for ReadOnly before attempting to normalize.
                        // - ReadOnly is fairly rare, but should be handled.
                        // - We need to be able to delete files with Deny WriteAttributes set because
                        // the cache sets that on files; at the same time, it conveniently prevents
                        // those same files from being ReadOnly.
                        try
                        {
                            RemoveReadonlyFlag(path);
                            return Unit.Void;
                        }
                        catch (Win32Exception ex)
                        {
                            if (ex.NativeErrorCode == NativeIOConstants.ErrorFileNotFound)
                            {
                                // It is considered a success if the file no longer exists when we try to reset attributes
                                return Unit.Void;
                            }
                            else if (ex.NativeErrorCode == NativeIOConstants.ErrorAccessDenied)
                            {
                                Logger.Log.FileUtilitiesDiagnostic(
                                    context,
                                    path,
                                    "Readonly attribute could not be set due to AccessDenied. Attempting to set WriteAttributes ACL for file.");

                                // Note: Modifications to the WriteAttributes permission is temporary only during the
                                // duration that the readonly attribute is modified. It is critical to reset it back to
                                // denying WriteAttributes BEFORE deleting the file, so other hardlinks to the same file
                                // are still denied access to writing the attributes
                                if (TrySetWriteAttributesPermission(path, AccessControlType.Allow))
                                {
                                    Logger.Log.FileUtilitiesDiagnostic(context, path, "Successfully set WriteAttributes ACL. Trying to remove readonly attribute again");
                                    RemoveReadonlyFlag(path);
                                    Logger.Log.FileUtilitiesDiagnostic(context, path, "Successfully set readonly attribute. Resetting WriteAttributes ACL to deny.");
                                    TrySetWriteAttributesPermission(path, AccessControlType.Deny);
                                }

                                // Access denied when resetting attributes will also manifest as an access denied when
                                // actually attempting to delete the file below. We let it get reported there since
                                // deleting the file is the actual intent
                                return Unit.Void;
                            }

                            throw;
                        }
                    },
                    ex => { throw new BuildXLException("Deleting a file failed (could not reset attributes)", ex); });

                deleteResult = TryOpenForDeletion(path);
                if (deleteResult.Succeeded || IsNonExistentResult(deleteResult))
                {
                    Logger.Log.FileUtilitiesDiagnostic(context, path, "Successfully deleted file after resetting readonly attribute");
                    return true;
                }
            }

            // The file could be opened for execution. This is another trick to delete it
            if (deleteResult.Status == OpenFileStatus.AccessDenied)
            {
                // Before attempting new method, log failure of previous attempt
                Logger.Log.FileUtilitiesDiagnostic(
                    context,
                    path,
                    I($"File deletion failed after removing readonly flag with native error code: {deleteResult.NativeErrorCode}. Attempting to delete file via MoveReplacement."));

                if (TryDeleteViaMoveReplacement(path))
                {
                    Logger.Log.FileUtilitiesDiagnostic(context, path, "Successfully deleted file via MoveReplacement");
                    return true;
                }
            }

            // Finally, it might be necessary to set the ACL on the file before deleting
            if (deleteResult.Status == OpenFileStatus.AccessDenied)
            {
                // Before attempting new method, log failure of previous attempt
                Logger.Log.FileUtilitiesDiagnostic(context, path, "Failed to delete via MoveReplacement. Attempting to take ownership of file.");
                if (TryTakeOwnershipAndSetAcl(path))
                {
                    Logger.Log.FileUtilitiesDiagnostic(context, path, "Successfully took ownership of file. Attempting delete again.");
                    deleteResult = TryOpenForDeletion(path);
                    if (deleteResult.Succeeded)
                    {
                        Logger.Log.FileUtilitiesDiagnostic(context, path, "Successfully deleted file.");
                        return true;
                    }
                }
            }

            // Attempt to move the file somewhere else to be deleted later
            if (deleteResult.Status == OpenFileStatus.AccessDenied || deleteResult.Status == OpenFileStatus.SharingViolation)
            {
                // There must be an IFileSystemCleaner instance with an associated temp directory to move to
                // and do not attempt move-deletes to the same directory
                // Note: We require the cleaner rather than just a temp directory to ensure the directory will be cleaned later
                string deletionTempDirectory = tempDirectoryCleaner?.TempDirectory;
                if (!string.IsNullOrWhiteSpace(deletionTempDirectory) && !path.Contains(deletionTempDirectory))
                {
                    Logger.Log.FileUtilitiesDiagnostic(
                        context,
                        path,
                        $"File deletion failed after taking ownership of file with native error code: {deleteResult.NativeErrorCode}. Attempting move-delete.");
                    if (TryMoveDelete(path, deletionTempDirectory))
                    {
                        Logger.Log.FileUtilitiesDiagnostic(context, path, "Successfully move-deleted file.");
                        return true;
                    }
                }
            }

            if (PosixDeleteMode == PosixDeleteMode.RunLast && RunPosixDelete(path, out deleteResult))
            {
                return true;
            }

            if (!Exists(path))
            {
                // Known cases where this can occur:
                // 1. The file never existed in the first place. We assume this is not true in most cases and most file deletion calls will have returned by now.
                // 2. Another command deleted the file.
                // 3. We successfully deleted the file at some point, but the OS put the delete action on a background thread until now. Windows can succesfully
                // mark a file for deletion but wait until all handles to a file are closed before deleting a file.
                Logger.Log.FileUtilitiesDiagnostic(
                    Events.StaticContext,
                    path,
                    "The file does not exist. Something may have successfully deleted file in the background.");
                return true;
            }

            // File deletion could fail because a handle is open to the file.
            // Note: All delete attempts fail in non-RS3 Windows versions on all hardlinks to the same file if a single one is open without FileShare Delete mode
            Logger.Log.FileUtilitiesDiagnostic(context, path, "Exhausted all backup methods of deleting file. Deletion may be reattempted in outer retry loop.");
            return false;
        }

        private bool RunPosixDelete(string path, out OpenFileResult deleteResult)
        {
            if (PosixDeleteMode == PosixDeleteMode.RunLast)
            {
                // We run as fallback.
                Logger.Log.FileUtilitiesDiagnostic(Events.StaticContext, path, "Run POSIX delete as a fallback.");
            }

            if (s_fileSystem.TryPosixDelete(path, out deleteResult))
            {
                return true;
            }

            // Check for deleting absent file on first attempt only
            if (deleteResult.Status == OpenFileStatus.FileNotFound)
            {
                return true;
            }

            return false;
        }

        private static void RemoveReadonlyFlag(string path)
        {
            FileAttributes fileAttr = s_fileSystem.GetFileAttributes(path);
            if ((fileAttr & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                s_fileSystem.SetFileAttributes(
                    path,
                    ((fileAttr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) ?
                    FileAttributes.Normal | FileAttributes.ReparsePoint : FileAttributes.Normal);
            }
        }

        private static bool TrySetWriteAttributesPermission(string path, AccessControlType control)
        {
            try
            {
                FileSystemAccessRule denyWriteAttribute = new FileSystemAccessRule(
                    s_worldSid,
                    FileSystemRights.WriteAttributes,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Deny);
                FileSystemAccessRule allowWriteAttribute = new FileSystemAccessRule(
                    s_worldSid,
                    FileSystemRights.WriteAttributes,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow);

                FileInfo fileInfo = new FileInfo(path);

                FileSecurity security = fileInfo.GetAccessControl();
                if (control == AccessControlType.Allow)
                {
                    security.RemoveAccessRule(denyWriteAttribute);
                    security.AddAccessRule(allowWriteAttribute);
                }
                else
                {
                    security.RemoveAccessRule(allowWriteAttribute);
                    security.AddAccessRule(denyWriteAttribute);
                }

                fileInfo.SetAccessControl(security);
            }
            catch (Exception ex)
            {
                Logger.Log.FileUtilitiesDiagnostic(Events.StaticContext, path, "Failed to set WriteAttributes permission: " + ex.GetLogEventMessage());
                return false;
            }

            return true;
        }

        private static bool TryTakeOwnershipAndSetAcl(string path)
        {
            if (path.StartsWith(FileSystemWin.LongPathPrefix, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(FileSystemWin.LocalDevicePrefix, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(FileSystemWin.NtPathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(4, path.Length - 4);
            }

            Logger.Log.SettingOwnershipAndAcl(Events.StaticContext, path);

            // It is possible that TakeOwn fails, but ICACLS will still succeed, so do not return false on failure
            StartProcess("takeown", I($"/F \"{path}\""));

            string userSid = WindowsIdentity.GetCurrent().User?.Value;
            if (userSid != null)
            {
                // SID must be prefixed with '*' char
                return StartProcess("icacls", I($"\"{path}\" /grant *{userSid}:(F)"));
            }

            // could not get user's SID -> fall back to using username
            return StartProcess("icacls", I($"\"{path}\" /grant {Environment.UserName}:(F)"));


            bool StartProcess(string processName, string arguments)
            {
                var process = Process.Start(new ProcessStartInfo()
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    FileName = processName,
                    Arguments = arguments
                });
                process.WaitForExit(60 * 1000);

                if (process.ExitCode != 0)
                {
                    Logger.Log.SettingOwnershipAndAclFailed(
                        Events.StaticContext,
                        path,
                        process.StartInfo.FileName,
                        process.StartInfo.Arguments,
                        process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd());
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Launches ICACLS to get the ACL of a path
        /// </summary>
        /// <returns>
        /// A string representing the ACL of the path or null if ICACLS fails
        /// </returns>
        private static string GetACL(string path)
        {
            Process icacls = Process.Start(new ProcessStartInfo()
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                FileName = "icacls",
                Arguments = path
            });
            icacls.WaitForExit(60 * 1000);

            string acl = null;
            if (icacls.ExitCode == 0)
            {
                acl = icacls.StandardOutput.ReadToEnd();
            }

            return acl;
        }

        /// <inheritdoc />
        public bool TryMoveDelete(string path, string deletionTempDirectory)
        {
            // Create a move target under TempDirectory
            // TempDirectory will be cleaned in the background by TempCleaner thread
            string destination = deletionTempDirectory + Path.DirectorySeparatorChar + Guid.NewGuid().ToString();

            SafeFileHandle srcHandle;

            // Requires delete access on path that will be target of TryRename
            OpenFileResult openResult = s_fileSystem.TryCreateOrOpenFile(
                path,
                FileDesiredAccess.Delete,
                FileShare.ReadWrite | FileShare.Delete,
                FileMode.Open,
                FileFlagsAndAttributes.FileFlagBackupSemantics,
                out srcHandle);

            using (srcHandle)
            {
                if (!openResult.Succeeded)
                {
                    Logger.Log.FileUtilitiesDiagnostic(
                        Events.StaticContext,
                        path,
                        I($"Opening file for file move failed with native error code: {openResult.NativeErrorCode}"));
                    return false;
                }

                if (!s_fileSystem.TryRename(srcHandle, destination, replaceExisting: true))
                {
                    Logger.Log.FileUtilitiesDiagnostic(
                        Events.StaticContext,
                        path,
                        I($"File moved failed with native error code: {Marshal.GetLastWin32Error()}"));
                    return false;
                }
            }

            // Ensure move completed successfully
            if (Exists(path) || !Exists(destination))
            {
                Logger.Log.FileUtilitiesDiagnostic(Events.StaticContext, path, "File move failed, but no native error occurred.");
                return false;
            }

            // Attempt to delete the moved file immmediately, otherwise TempCleaner will do so in background
            TryOpenForDeletion(destination);

            // Either way, move was successful and the file at original path is gone
            return true;
        }

        private static bool IsNonExistentResult(OpenFileResult deleteResult)
        {
            return deleteResult.Status.IsNonexistent();
        }

        /// <inheritdoc />
        public void SetFileTimestamps(string path, FileTimestamps timestamps, bool followSymlink)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(path));

            SafeFileHandle handle;
            OpenFileResult openResult = s_fileSystem.TryCreateOrOpenFile(
                path,
                FileDesiredAccess.FileWriteAttributes,
                FileShare.ReadWrite | FileShare.Delete,
                FileMode.Open,
                followSymlink ? FileFlagsAndAttributes.None : FileFlagsAndAttributes.FileFlagOpenReparsePoint,
                out handle);

            using (handle)
            {
                if (!openResult.Succeeded)
                {
                    throw new BuildXLException("Failed to open a file to set its timestamps.", openResult.CreateExceptionForError());
                }

                s_fileSystem.SetFileTimestampsByHandle(
                    handle,
                    timestamps.CreationTime,
                    timestamps.AccessTime,
                    timestamps.LastWriteTime,
                    timestamps.LastChangeTime);
            }
        }

        /// <inheritdoc />
        public FileTimestamps GetFileTimestamps(
            string path,
            bool followSymlink)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(path));

            SafeFileHandle handle;
            OpenFileResult openResult = s_fileSystem.TryCreateOrOpenFile(
                path,
                FileDesiredAccess.FileReadAttributes,
                FileShare.Read | FileShare.Delete,
                FileMode.Open,
                followSymlink ? FileFlagsAndAttributes.None : FileFlagsAndAttributes.FileFlagOpenReparsePoint,
                out handle);

            using (handle)
            {
                if (!openResult.Succeeded)
                {
                    throw new BuildXLException("Failed to open a file to get its timestamps.", openResult.CreateExceptionForError());
                }

                s_fileSystem.GetFileTimestampsByHandle(
                    handle,
                    out var creationTime,
                    out var accessTime,
                    out var lastWriteTime,
                    out var lastChangeTime);

                return new FileTimestamps(
                    creationTime: creationTime,
                    accessTime: accessTime,
                    lastWriteTime: lastWriteTime,
                    lastChangeTime: lastChangeTime);
            }
        }

        /// <summary>
        /// Tries to open a file for deletion
        /// </summary>
        /// <remarks>
        /// Supports paths greater than MAX_PATH.
        /// </remarks>
        private static OpenFileResult TryOpenForDeletion(string path)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));

            // FILE_FLAG_DELETE_ON_CLOSE has experimentally been viable for removing links to files
            // with memory-mapped sections. DeleteFileW instead uses SetInformationByHandle(FileDispositionInfo)
            // which fails in those cases. Note that this function does NOT work for unlinking running executable
            // images. See TryDeleteViaMoveReplacement.
            SafeFileHandle handle;
            OpenFileResult openResult = s_fileSystem.TryCreateOrOpenFile(
                path,
                FileDesiredAccess.Delete,
                FileShare.Read | FileShare.Write | FileShare.Delete,
                FileMode.Open,
                FileFlagsAndAttributes.FileFlagDeleteOnClose | FileFlagsAndAttributes.FileFlagOpenReparsePoint | FileFlagsAndAttributes.FileFlagBackupSemantics,
                out handle);
            using (handle)
            {
                return openResult;
            }
        }

        /// <inheritdoc />
        public Task WriteAllTextAsync(
            string filePath,
            string text,
            Encoding encoding)
        {
            Contract.Requires(!string.IsNullOrEmpty(filePath));
            Contract.Requires(text != null);
            Contract.Requires(encoding != null);

            // TODO: EnsuresOnThrow rewriting does a bad thing to some methods returning Task. Following up with mbarnett.
            // Contract.EnsuresOnThrow<BuildXLException>(true);
            byte[] bytes = encoding.GetBytes(text);
            return WriteAllBytesAsync(filePath, bytes);
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "bytes")]
        public Task<bool> WriteAllBytesAsync(
            string filePath,
            byte[] bytes,
            Func<SafeFileHandle, bool> predicate = null,
            Action<SafeFileHandle> onCompletion = null)
        {
            Contract.Requires(!string.IsNullOrEmpty(filePath));
            Contract.Requires(bytes != null);

            return ExceptionUtilities.HandleRecoverableIOExceptionAsync(
                async () =>
                {
                    if (predicate != null)
                    {
                        SafeFileHandle destinationHandle;
                        OpenFileResult predicateQueryOpenResult = s_fileSystem.TryCreateOrOpenFile(
                            filePath,
                            FileDesiredAccess.GenericRead,
                            FileShare.Read | FileShare.Delete,
                            FileMode.OpenOrCreate,
                            FileFlagsAndAttributes.None,
                            out destinationHandle);
                        using (destinationHandle)
                        {
                            if (!predicateQueryOpenResult.Succeeded)
                            {
                                throw new BuildXLException(
                                    "Failed to open a file to check its version",
                                    predicateQueryOpenResult.CreateExceptionForError());
                            }

                            if (!predicate(predicateQueryOpenResult.OpenedOrTruncatedExistingFile ? destinationHandle : null))
                            {
                                return false;
                            }
                        }
                    }

                    using (FileStream stream = CreateReplacementFile(
                        filePath,
                        FileShare.Delete,
                        openAsync: true))
                    {
                        await stream.WriteAsync(bytes, 0, bytes.Length);

                        onCompletion?.Invoke(stream.SafeFileHandle);
                    }

                    return true;
                },
                ex => { throw new BuildXLException("File write failed", ex); });
        }

        /// <inheritdoc />
        public Task<bool> CopyFileAsync(
            string source,
            string destination,
            Func<SafeFileHandle, SafeFileHandle, bool> predicate = null,
            Action<SafeFileHandle, SafeFileHandle> onCompletion = null)
        {
            Contract.Requires(!string.IsNullOrEmpty(source));
            Contract.Requires(!string.IsNullOrEmpty(destination));

            // TODO: EnsuresOnThrow rewriting does a bad thing to some methods returning Task. Following up with mbarnett.
            // Contract.EnsuresOnThrow<BuildXLException>(true);

            // TODO: Should log (source, destination, size) and an end marker.
            return ExceptionUtilities.HandleRecoverableIOExceptionAsync(
                async () =>
                {
                    using (FileStream sourceStream = CreateAsyncFileStream(
                        source,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read | FileShare.Delete))
                    {
                        if (predicate != null)
                        {
                            SafeFileHandle destinationHandle;
                            OpenFileResult predicateQueryOpenResult = s_fileSystem.TryCreateOrOpenFile(
                                destination,
                                FileDesiredAccess.GenericRead,
                                FileShare.Read | FileShare.Delete,
                                FileMode.OpenOrCreate,
                                FileFlagsAndAttributes.None,
                                out destinationHandle);
                            using (destinationHandle)
                            {
                                if (!predicateQueryOpenResult.Succeeded)
                                {
                                    throw new BuildXLException(
                                        "Failed to open a copy destination to check its version",
                                        predicateQueryOpenResult.CreateExceptionForError());
                                }

                                if (!predicate(sourceStream.SafeFileHandle, predicateQueryOpenResult.OpenedOrTruncatedExistingFile ? destinationHandle : null))
                                {
                                    return false;
                                }
                            }
                        }

                        using (FileStream destinationStream = CreateReplacementFile(destination, FileShare.Delete, openAsync: true))
                        {
                            await sourceStream.CopyToAsync(destinationStream);
                            onCompletion?.Invoke(sourceStream.SafeFileHandle, destinationStream.SafeFileHandle);
                        }
                    }

                    return true;
                },
                ex => { throw new BuildXLException("File copy failed", ex); });
        }

        /// <inheritdoc />
        public Task<bool> MoveFileAsync(
            string source,
            string destination,
            bool replaceExisting = false)
        {
            Contract.Requires(!string.IsNullOrEmpty(source));
            Contract.Requires(!string.IsNullOrEmpty(destination));

            return Task.Run<bool>(
                () => ExceptionUtilities.HandleRecoverableIOException(
                    () =>
                    {
                        return s_fileSystem.MoveFile(source, destination, replaceExisting);
                    },
                    ex => { throw new BuildXLException("File move failed", ex); }));
        }

        /// <inheritdoc />
        public void CloneFile(string source, string destination, bool followSymlink) => throw new NotImplementedException();

        /// <inheritdoc />
        public FileStream CreateAsyncFileStream(
            string path,
            FileMode fileMode,
            FileAccess fileAccess,
            FileShare fileShare,
            FileOptions options = FileOptions.None,
            bool force = false,
            bool allowExcludeFileShareDelete = false)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));
            Contract.Requires(allowExcludeFileShareDelete || ((fileShare & FileShare.Delete) != 0));
            Contract.EnsuresOnThrow<BuildXLException>(true);

            return CreateFileStream(
                path,
                fileMode,
                fileAccess,
                fileShare,
                options | FileOptions.Asynchronous,
                force: force,
                allowExcludeFileShareDelete: allowExcludeFileShareDelete);
        }

        /// <inheritdoc />
        public FileStream CreateFileStream(
            string path,
            FileMode fileMode,
            FileAccess fileAccess,
            FileShare fileShare,
            FileOptions options = FileOptions.None,
            bool force = false,
            bool allowExcludeFileShareDelete = false)
        {
            Contract.Requires(allowExcludeFileShareDelete || ((fileShare & FileShare.Delete) != 0));
            Contract.EnsuresOnThrow<BuildXLException>(true);

            return s_fileSystem.CreateFileStream(path, fileMode, fileAccess, fileShare, options, force);
        }

        /// <inheritdoc />
        public TResult UsingFileHandleAndFileLength<TResult>(
            string path,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileMode creationDisposition,
            FileFlagsAndAttributes flagsAndAttributes,
            Func<SafeFileHandle, long, TResult> handleStream)
        {
            var openResult = s_fileSystem.TryCreateOrOpenFile(
                       path,
                       desiredAccess,
                       shareMode,
                       creationDisposition,
                       flagsAndAttributes,
                       out var handle);

            if (!openResult.Succeeded)
            {
                var failure = openResult.CreateFailureForError();
                if (openResult.NativeErrorCode == NativeIOConstants.ErrorSharingViolation)
                {
                    if (TryFindOpenHandlesToFile(path, out var info))
                    {
                        failure.Annotate(info);
                    }
                    else
                    {
                        failure.Annotate("TryFindOpenHandlesToFile failed.");
                    }
                }

                failure.Throw();
            }

            using (handle)
            {
                Contract.Assert(handle != null && !handle.IsInvalid);

                using (var file = new AsyncFileWin(handle, desiredAccess, ownsHandle: false, path: path))
                using (var stream = file.CreateReadableStream())
                {
                    return handleStream(handle, stream.Length);
                }
            }
        }

        /// <inheritdoc />
        public FileStream CreateReplacementFile(
            string path,
            FileShare fileShare,
            bool openAsync = true,
            bool allowExcludeFileShareDelete = false)
        {
            Contract.Requires(allowExcludeFileShareDelete || ((fileShare & FileShare.Delete) != 0));
            Contract.Requires(path != null);

            // We are immediately re-creating this path, therefore it is vital to ensure the delete is totally done
            // (otherwise, we may transiently fail to recreate the path with ERROR_ACCESS_DENIED; see DeleteFile remarks)
            DeleteFile(path, waitUntilDeletionFinished: true);
            return openAsync
                ? CreateAsyncFileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, fileShare, allowExcludeFileShareDelete: allowExcludeFileShareDelete)
                : CreateFileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, fileShare, allowExcludeFileShareDelete: allowExcludeFileShareDelete);
        }

        /// <summary>
        /// Attempts to create a temporary file and replace the file at <paramref name="destinationPath"/> with it.
        /// Upon exit, the temporary file will have been deleted. If 'true' is returned, then the file at <paramref name="destinationPath"/>
        /// has been deleted as well.
        /// This operation will fail if the target file has the 'readonly' attribute.
        /// </summary>
        /// <remarks>
        /// This seems like a peculiar way to delete a file. Why not just call <c>DeleteFile</c>?
        /// Experimentally, this is the most foolproof way to delete a link to a file when that file is mapped for execution
        /// in some process. It also works in most other cases, except when the file has a *writable* mapped section.
        /// </remarks>
        private static bool TryDeleteViaMoveReplacement(string destinationPath)
        {
            const int MaxTemporaryFileCreationAttempts = 20;

            SafeFileHandle temporaryHandle = null;
            int lastDirEnd = destinationPath.LastIndexOf(Path.DirectorySeparatorChar);
            Contract.Assume(lastDirEnd != -1, "Destination path must be absolute");
            string dirPath = destinationPath.Substring(0, lastDirEnd);

            var rng = new Random(destinationPath.GetHashCode());
            for (int i = 0; i < MaxTemporaryFileCreationAttempts; i++)
            {
                string temporaryPath = I($"{dirPath}\\~DT-{unchecked((ushort) rng.Next()):X4}");

                OpenFileResult openResult = s_fileSystem.TryCreateOrOpenFile(
                    temporaryPath,
                    FileDesiredAccess.Delete,
                    FileShare.Delete,
                    FileMode.CreateNew,
                    FileFlagsAndAttributes.FileFlagDeleteOnClose | FileFlagsAndAttributes.FileAttributeHidden,
                    out temporaryHandle);

                if (!openResult.Succeeded)
                {
                    Contract.Assert(temporaryHandle == null);

                    // If the parent directory doesn't exist, the file to delete (in the parent directory also) must not either
                    if (IsNonExistentResult(openResult))
                    {
                        return true;
                    }
                }
                else
                {
                    Contract.Assert(temporaryHandle != null);
                    break;
                }
            }

            using (temporaryHandle)
            {
                if (temporaryHandle == null)
                {
                    // TODO: Event
                    return false;
                }

                bool success = s_fileSystem.TryRename(temporaryHandle, destinationPath, replaceExisting: true);
                if (!success)
                {
                    Logger.Log.FileUtilitiesDiagnostic(
                        Events.StaticContext,
                        destinationPath,
                        I($"Renaming file failed with native error code: {Marshal.GetLastWin32Error()}"));
                }

                return success;
            }
        }

        /// <inheritdoc />
        public Possible<string> GetFileName(string path)
        {
            try
            {
                return s_fileSystem.GetFileName(path);
            }
            catch (NativeWin32Exception exception)
            {
                return new Failure<string>("Failed to get file name", NativeFailure.CreateFromException(exception));
            }
        }

        /// <inheritdoc />
        public string GetKnownFolderPath(Guid knownFolder)
        {
            string path = string.Empty;
            IntPtr pathPtr = IntPtr.Zero;

            // "Do not verify the folder's existence before attempting to retrieve the path or IDList.
            // If this flag is not set, an attempt is made to verify that the folder is truly present at the path.
            // If that verification fails due to the folder being absent or inaccessible,
            // the function returns a failure code and no path is returned."
            // (see https://docs.microsoft.com/en-us/windows/desktop/api/shlobj_core/ne-shlobj_core-known_folder_flag)
            const int KF_FLAG_DONT_VERIFY = 0x00004000;
            const int E_INVALIDARG = unchecked((int)0x80070057);
            const int E_FAIL = unchecked((int)0x80004005);

            int hr = NativeMethods.SHGetKnownFolderPath(ref knownFolder, KF_FLAG_DONT_VERIFY, IntPtr.Zero, out pathPtr);
            if (hr == 0)
            {
                if (pathPtr != IntPtr.Zero)
                {
                    path = Marshal.PtrToStringUni(pathPtr);
                    Marshal.FreeCoTaskMem(pathPtr);
                }
            }
            else
            {
                if (knownFolder == FileUtilities.KnownFolderLocalLow)
                {
                    // Failed to get a path for LocalLow. This should not happen, unfortunately, sometimes it does fail.
                    // Do some extra logging before throwing an exception to facilitate future investigation into this problem.
                    string expectedLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify), "AppData", "LocalLow");
                    bool localLowExists = FileUtilities.DirectoryExistsNoFollow(expectedLocation);

                    throw new BuildXLException(
                        I($"Failed to get a path for the known GUID '{knownFolder.ToString("D")}' (Known GUID for LocalLow). Path '{expectedLocation}' exists: '{localLowExists}'. HResult='{(hr == E_INVALIDARG ? "E_INVALIDARG" : (hr == E_FAIL ? "E_FAIL" : hr.ToString()))}'"), 
                        new NativeWin32Exception(hr));
                }
                
                throw new BuildXLException(I($"Failed to get a path for the known GUID '{knownFolder.ToString("D")}'"), new NativeWin32Exception(hr));
            }

            return path;
        }

        private static class NativeMethods
        {
            [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
            public static extern int SHGetKnownFolderPath(ref Guid rfid, int dwFlags, IntPtr hToken, out IntPtr lpszPath);
        }

        /// <inheritdoc />
        public string GetUserSettingsFolder(string appName)
        {
            Contract.Requires(!string.IsNullOrEmpty(appName));

#if FEATURE_CORECLR
            var homeFolder = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (string.IsNullOrEmpty(homeFolder))
            {
                homeFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
#else
            var homeFolder = SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
#endif
            var settingsFolder = Path.Combine(homeFolder, ToPascalCase(appName));

            s_fileSystem.CreateDirectory(settingsFolder);
            return settingsFolder;
        }

        private static string ToPascalCase(string name)
        {
            return char.IsUpper(name[0])
                ? name
                : char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        /// <inheritdoc />
        public bool TryFindOpenHandlesToFile(string filePath, out string diagnosticInfo, bool printCurrentFilePath = true)
        {
            using (var pool = Pools.GetStringBuilder())
            {
                StringBuilder builder = pool.Instance;
                if (printCurrentFilePath)
                {
                    builder.Append(FileUtilitiesMessages.ActiveHandleUsage + filePath);
                    builder.AppendLine();
                }

                if (HandleSearcher.GetProcessIdsUsingHandle(filePath, out var processIds))
                {
                    foreach (var pid in processIds)
                    {
                        try
                        {
                            Process process = Process.GetProcessById(pid);

                            builder.AppendFormat("Handle was used by '{0}' with PID '{1}'.", process.ProcessName, pid);
                            builder.AppendLine();
                        }
                        catch (Exception ex)
                        {
                            if (ex is InvalidOperationException || ex is ArgumentException)
                            {
                                // The process is no longer running
                                builder.AppendFormat("Handle was used by process with PID '{0}'. That process is no longer running.", pid);
                                builder.AppendLine();
                            }
                            else
                            {
                                diagnosticInfo = ex.GetLogEventMessage();
                                return false;
                            }
                        }
                    }

                    diagnosticInfo = processIds.Count == 0 ? FileUtilitiesMessages.NoProcessesUsingHandle : builder.ToString();

                    return true;
                }

                diagnosticInfo = string.Empty;
                return false;
            }
        }

        /// <inheritdoc />
        public uint GetHardLinkCount(string path)
        {
            SafeFileHandle handle;
            OpenFileResult openResult = s_fileSystem.TryCreateOrOpenFile(
                path,
                FileDesiredAccess.GenericRead,
                FileShare.Read | FileShare.Delete,
                FileMode.Open,
                FileFlagsAndAttributes.None,
                out handle);
            using (handle)
            {
                if (!openResult.Succeeded)
                {
                    throw new BuildXLException("Failed to open a file to get its hard link count.", openResult.CreateExceptionForError());
                }

                return s_fileSystem.GetHardLinkCountByHandle(handle);
            }
        }

        /// <inheritdoc />
        public bool HasWritableAccessControl(string path)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(path));
            path = FileSystemWin.ToLongPathIfExceedMaxPath(path);

#if !FEATURE_CORECLR
            FileSystemRights fileSystemRights =
                FileSystemRights.WriteData |
                FileSystemRights.AppendData |
                FileSystemRights.WriteAttributes |
                FileSystemRights.WriteExtendedAttributes;

            FileInfo fileInfo = new FileInfo(path);
            FileSecurity fileSecurity = fileInfo.GetAccessControl();
            foreach (AuthorizationRule rule in fileSecurity.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                var identifier = rule.IdentityReference as SecurityIdentifier;

                if (identifier != null && identifier == s_worldSid)
                {
                    var fileSystemAccessRule = rule as FileSystemAccessRule;
                    if (fileSystemAccessRule != null &&
                        (fileSystemAccessRule.FileSystemRights & fileSystemRights) != 0 &&
                        fileSystemAccessRule.AccessControlType == AccessControlType.Deny)
                    {
                        return false;
                    }
                }
            }

            return true;
#else
            return true;
#endif
        }

        /// <inheritdoc />
        public void SetFileAccessControl(string path, FileSystemRights fileSystemRights, bool allow)
        {
            path = FileSystemWin.ToLongPathIfExceedMaxPath(path);
            var denyWriteRule = new FileSystemAccessRule(
                FileUtilitiesWin.s_worldSid,
                fileSystemRights,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Deny);
            try
            {
                FileInfo fileInfo = new FileInfo(path);

                FileSecurity security = fileInfo.GetAccessControl();

                if (allow)
                {
                    security.RemoveAccessRule(denyWriteRule);
                }
                else
                {
                    security.AddAccessRule(denyWriteRule);
                }

                // For some bizarre reason, instead using SetAccessControl on the caller's existing FileStream fails reliably.
                fileInfo.SetAccessControl(security);
            }
            catch (ArgumentException e)
            {
                // calls to GetAccessControl sometime result in a weird ArgumentException
                // add more data to the exception so we could find some pattern if any
                throw new ArgumentException(I($"SetFileAccessControl arguments -- path: '{path}', FileSystemRights: {fileSystemRights}, allow: {allow}"), e);
            }
        }
    }
}
