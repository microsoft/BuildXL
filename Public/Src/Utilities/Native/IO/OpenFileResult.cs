// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Native.IO.Windows;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Represents the result of attempting to open a file (such as with <see cref="IFileSystem.TryCreateOrOpenFile(string, FileDesiredAccess, System.IO.FileShare, System.IO.FileMode, FileFlagsAndAttributes, out Microsoft.Win32.SafeHandles.SafeFileHandle)"/>).
    /// </summary>
    public readonly struct OpenFileResult : IEquatable<OpenFileResult>
    {
        /// <summary>
        /// Native error code.
        /// </summary>
        /// <remarks>
        /// This is the same as returned by <c>GetLastError</c>, except when it is not guaranteed to be set; then it is normalized to
        /// <c>ERROR_SUCCESS</c>
        /// </remarks>
        public int NativeErrorCode { get; }

        /// <summary>
        /// Normalized status indication (derived from <see cref="NativeErrorCode"/> and the creation disposition).
        /// </summary>
        /// <remarks>
        /// This is useful for two reasons: it is an enum for which we can know all cases are handled, and successful opens
        /// are always <see cref="OpenFileStatus.Success"/> (the distinction between opening / creating files is moved to
        /// <see cref="OpenedOrTruncatedExistingFile"/>)
        /// </remarks>
        public OpenFileStatus Status { get; }

        /// <summary>
        /// Indicates if an existing file was opened (or truncated). For creation dispositions such as <see cref="System.IO.FileMode.OpenOrCreate"/>,
        /// either value is possible on success. On failure, this is always <c>false</c> since no file was opened.
        /// </summary>
        public bool OpenedOrTruncatedExistingFile { get; }

        /// <summary>
        /// The path of the file that was opened. Null if the path was opened by <see cref="FileId"/>.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Creates an <see cref="OpenFileResult"/> without any normalization from native error code.
        /// </summary>
        private OpenFileResult(string path, OpenFileStatus status, int nativeErrorCode, bool openedOrTruncatedExistingFile)
        {
            Path = path;
            Status = status;
            NativeErrorCode = nativeErrorCode;
            OpenedOrTruncatedExistingFile = openedOrTruncatedExistingFile;
        }

        /// <summary>
        /// Creates an <see cref="OpenFileResult"/> from observed return values from a native function.
        /// Used when opening files by <see cref="FileId"/> to handle quirky error codes.
        /// </summary>
        public static OpenFileResult CreateForOpeningById(int nativeErrorCode, FileMode creationDisposition, bool handleIsValid)
        {
            return Create(path: null, nativeErrorCode, creationDisposition, handleIsValid, openingById: true);
        }

        /// <summary>
        /// Creates an <see cref="OpenFileResult"/> from observed return values from a native function.
        /// </summary>
        public static OpenFileResult Create(string path, int nativeErrorCode, FileMode creationDisposition, bool handleIsValid)
        {
            return Create(path, nativeErrorCode, creationDisposition, handleIsValid, openingById: false);
        }

        /// <summary>
        /// Creates an <see cref="OpenFileResult"/> from observed return values from a native function.
        /// </summary>
        /// <remarks>
        /// <paramref name="openingById"/> is needed since <c>OpenFileById</c> has some quirky error codes.
        /// </remarks>
        private static OpenFileResult Create(string path, int nativeErrorCode, FileMode creationDisposition, bool handleIsValid, bool openingById)
        {
            // Here's a handy table of various FileModes, corresponding dwCreationDisposition, and their properties:
            // See http://msdn.microsoft.com/en-us/library/windows/desktop/aa363858(v=vs.85).aspx
            // Managed FileMode | Creation disp.    | Error always set? | Distinguish existence?    | Existing file on success?
            // ----------------------------------------------------------------------------------------------------------------
            // Append           | OPEN_ALWAYS       | 1                 | 1                         | 0
            // Create           | CREATE_ALWAYS     | 1                 | 1                         | 0
            // CreateNew        | CREATE_NEW        | 0                 | 0                         | 0
            // Open             | OPEN_EXISTING     | 0                 | 0                         | 1
            // OpenOrCreate     | OPEN_ALWAYS       | 1                 | 1                         | 0
            // Truncate         | TRUNCATE_EXISTING | 0                 | 0                         | 1
            //
            // Note that some modes always set a valid last-error, and those are the same modes
            // that distinguish existence on success (i.e., did we just create a new file or did we open one).
            // The others do not promise to set ERROR_SUCCESS and instead failure implies existence
            // (or absence) according to the 'Existing file on success?' column.
            bool modeDistinguishesExistence =
                creationDisposition == FileMode.OpenOrCreate ||
                creationDisposition == FileMode.Create ||
                creationDisposition == FileMode.Append;

            if (handleIsValid && !modeDistinguishesExistence)
            {
                nativeErrorCode = NativeIOConstants.ErrorSuccess;
            }

            OpenFileStatus status;
            var openedOrTruncatedExistingFile = false;

            switch (nativeErrorCode)
            {
                case NativeIOConstants.ErrorSuccess:
                    Contract.Assume(handleIsValid);
                    status = OpenFileStatus.Success;
                    openedOrTruncatedExistingFile = creationDisposition == FileMode.Open || creationDisposition == FileMode.Truncate;
                    break;
                case NativeIOConstants.ErrorFileNotFound:
                    Contract.Assume(!handleIsValid);
                    status = OpenFileStatus.FileNotFound;
                    break;
                case NativeIOConstants.ErrorPathNotFound:
                    Contract.Assume(!handleIsValid);
                    status = OpenFileStatus.PathNotFound;
                    break;
                case NativeIOConstants.ErrorAccessDenied:
                    Contract.Assume(!handleIsValid);
                    status = OpenFileStatus.AccessDenied;
                    break;
                case NativeIOConstants.ErrorSharingViolation:
                    Contract.Assume(!handleIsValid);
                    status = OpenFileStatus.SharingViolation;
                    break;
                case NativeIOConstants.ErrorNotReady:
                    status = OpenFileStatus.ErrorNotReady;
                    break;
                case NativeIOConstants.FveLockedVolume:
                    status = OpenFileStatus.FveLockedVolume;
                    break;
                case NativeIOConstants.ErrorInvalidParameter:
                    Contract.Assume(!handleIsValid);

                    // Experimentally, it seems OpenFileById throws ERROR_INVALID_PARAMETER if the file ID doesn't exist.
                    // This is very unfortunate, since that is also used for e.g. invalid sizes for FILE_ID_DESCRIPTOR. Oh well.
                    status = openingById ? OpenFileStatus.FileNotFound : OpenFileStatus.UnknownError;
                    break;
                case NativeIOConstants.ErrorFileExists:
                case NativeIOConstants.ErrorAlreadyExists:
                    if (!handleIsValid)
                    {
                        Contract.Assume(creationDisposition == FileMode.CreateNew);
                        status = OpenFileStatus.FileAlreadyExists;
                    }
                    else
                    {
                        Contract.Assert(modeDistinguishesExistence);
                        status = OpenFileStatus.Success;
                        openedOrTruncatedExistingFile = true;
                    }

                    break;
                case NativeIOConstants.ErrorTimeout:
                    status = OpenFileStatus.Timeout;
                    break;
                case NativeIOConstants.ErrorCantAccessFile:
                    status = OpenFileStatus.CannotAccessFile;
                    break;
                case NativeIOConstants.ErrorBadPathname:
                    status = OpenFileStatus.BadPathname;
                    break;
                default:
                    Contract.Assume(!handleIsValid);
                    status = OpenFileStatus.UnknownError;
                    break;
            }

            bool succeeded = status == OpenFileStatus.Success;
            Contract.Assert(succeeded || !openedOrTruncatedExistingFile);
            Contract.Assert(handleIsValid == succeeded);

            return new OpenFileResult(path, status, nativeErrorCode, openedOrTruncatedExistingFile);
        }

        /// <inheritdoc />
        public bool Succeeded => Status == OpenFileStatus.Success;

        /// <inheritdoc />
        public bool Equals(OpenFileResult other)
        {
            return other.NativeErrorCode == NativeErrorCode &&
                    other.OpenedOrTruncatedExistingFile == OpenedOrTruncatedExistingFile &&
                    other.Status == Status;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return NativeErrorCode + (OpenedOrTruncatedExistingFile ? 1 : 0) | ((short)Status << 16);
        }

        /// <nodoc />
        public static bool operator ==(OpenFileResult left, OpenFileResult right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(OpenFileResult left, OpenFileResult right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Set of extension methods for <see cref="OpenFileResult"/>.
    /// </summary>
    public static class OpenFileResultExtensions
    {
        /// <summary>
        /// Throws an exception if the native error code could not be canonicalized (a fairly exceptional circumstance).
        /// </summary>
        /// <remarks>
        /// This is a good <c>default:</c> case when switching on every possible <see cref="OpenFileStatus"/>
        /// </remarks>
        public static Exception ThrowForUnknownError(this OpenFileResult result)
        {
            Contract.Requires(result.Status == OpenFileStatus.UnknownError);
            throw result.CreateExceptionForError();
        }

        /// <summary>
        /// Throws an exception if the native error code was canonicalized (known and common, but not handled by the caller).
        /// </summary>
        public static Exception ThrowForKnownError(this OpenFileResult result)
        {
            Contract.Requires(result.Status != OpenFileStatus.UnknownError && result.Status != OpenFileStatus.Success);
            throw result.CreateExceptionForError();
        }

        /// <summary>
        /// Throws an exception for a failed open.
        /// </summary>
        public static Exception ThrowForError(this OpenFileResult result)
        {
            Contract.Requires(result.Status != OpenFileStatus.Success);
            throw result.Status == OpenFileStatus.UnknownError ? result.ThrowForUnknownError() : result.ThrowForKnownError();
        }

        /// <summary>
        /// Creates (but does not throw) an exception for this result. The result must not be successful.
        /// </summary>
        public static Exception CreateExceptionForError(this OpenFileResult result)
        {
            Contract.Requires(result.Status != OpenFileStatus.Success);
            return new NativeWin32Exception(result.NativeErrorCode, GetErrorOrFailureMessage(result));
        }

        /// <summary>
        /// Creates a <see cref="Failure"/> representing this result. The result must not be successful.
        /// </summary>
        public static Failure CreateFailureForError(this OpenFileResult result)
        {
            Contract.Requires(result.Status != OpenFileStatus.Success);
            return new NativeFailure(result.NativeErrorCode).Annotate(GetErrorOrFailureMessage(result));
        }

        /// <summary>
        /// Returns a string representing information about the <see cref="OpenFileResult"/> error.
        /// </summary>
        private static string GetErrorOrFailureMessage(OpenFileResult result)
        {
            var message = result.Status == OpenFileStatus.UnknownError 
                            ? "Opening a file handle failed" 
                            : I($"Opening a file handle failed: {result.Status:G}");

            if (result.Status.ImpliesOtherProcessBlockingHandle() && result.Path != null)
            {
                message += Environment.NewLine;
                message += FileUtilities.TryFindOpenHandlesToFile(result.Path, out var info)
                    ? info
                    : "Attempt to find processes with open handles to the file failed.";
            }

            return message;
        }
    }
}
