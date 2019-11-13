// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using JetBrains.Annotations;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Represents the result of attempting to enumerate a directory.
    /// </summary>
    public readonly struct EnumerateDirectoryResult : IEquatable<EnumerateDirectoryResult>
    {
        /// <summary>
        /// Enumerated directory.
        /// </summary>
        public string Directory { get; }

        /// <summary>
        /// Overall status indication.
        /// </summary>
        public EnumerateDirectoryStatus Status { get; }

        /// <summary>
        /// Native error code. Note that an error code other than <c>ERROR_SUCCESS</c> may be present even on success.
        /// </summary>
        public int NativeErrorCode { get; }

        /// <summary>
        /// When <see cref="Succeeded"/> is <code>false</code>, this property
        /// can optionally contain the exception that caused the error.
        /// </summary>
        [CanBeNull]
        public Exception ErrorCause { get; }

        /// <summary>
        /// Indicates if enumeration succeeded.
        /// </summary>
        public bool Succeeded => Status == EnumerateDirectoryStatus.Success;

        /// <nodoc />
        public EnumerateDirectoryResult(string directory, EnumerateDirectoryStatus status, int nativeErrorCode, Exception errorCause = null)
        {
            Contract.Requires(nativeErrorCode != 0 || errorCause == null); // nativeErrorCode == 0 ==> errorCause == null
            Directory = directory;
            Status = status;
            NativeErrorCode = nativeErrorCode;
            ErrorCause = errorCause;
        }

        /// <inheritdoc />
        public bool Equals(EnumerateDirectoryResult other)
        {
            return string.Equals(Directory, other.Directory) && Status == other.Status && NativeErrorCode == other.NativeErrorCode;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is null)
            {

                return false;
            }

            return obj is EnumerateDirectoryResult result && Equals(result);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(
                Directory?.GetHashCode() ?? 0,
                (int)Status,
                NativeErrorCode);
        }

        /// <nodoc />
        public static bool operator ==(EnumerateDirectoryResult left, EnumerateDirectoryResult right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(EnumerateDirectoryResult left, EnumerateDirectoryResult right)
        {
            return !left.Equals(right);
        }

        /// <nodoc />
        public static EnumerateDirectoryStatus TranslateHResult(int hr)
        {
            switch (hr)
            {
                case NativeIOConstants.ErrorFileNotFound:
                    // ERROR_FILE_NOT_FOUND means that no results were found for the given pattern.
                    // This shouldn't actually happen so long as we only support the trivial \* wildcard,
                    // since we expect to always match the magic . and .. entries.
                    return EnumerateDirectoryStatus.Success;

                case NativeIOConstants.ErrorPathNotFound:
                    return EnumerateDirectoryStatus.SearchDirectoryNotFound;

                case NativeIOConstants.ErrorDirectory:
                    return EnumerateDirectoryStatus.CannotEnumerateFile;

                case NativeIOConstants.ErrorAccessDenied:
                    return EnumerateDirectoryStatus.AccessDenied;

                default:
                    return EnumerateDirectoryStatus.UnknownError;
            }
        }

        /// <nodoc />
        public static EnumerateDirectoryResult CreateFromHResult(string directoryPath, int hr)
        {
            return new EnumerateDirectoryResult(directoryPath, TranslateHResult(hr), hr);
        }

        /// <nodoc />
        public static EnumerateDirectoryResult CreateFromException(string directoryPath, Exception ex)
        {
            var findHandleOpenStatus = ex switch
            {
                UnauthorizedAccessException _ => EnumerateDirectoryStatus.AccessDenied,
                System.IO.DirectoryNotFoundException _ => EnumerateDirectoryStatus.SearchDirectoryNotFound,
                System.IO.IOException _ => EnumerateDirectoryStatus.CannotEnumerateFile,
                _ => EnumerateDirectoryStatus.UnknownError,
            };

            return new EnumerateDirectoryResult(directoryPath, findHandleOpenStatus, ex.HResult, ex);
        }
    }
}
