// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Extension methods for <see cref="EnumerateDirectoryResult"/>.
    /// </summary>
    public static class EnumerateDirectoryResultExtensions
    {
        /// <summary>
        /// Throws an exception if the native error code could not be canonicalized (a fairly exceptional circumstance).
        /// This is allowed when <see cref="EnumerateDirectoryResult.Status"/> is <see cref="EnumerateDirectoryStatus.UnknownError"/>.
        /// </summary>
        /// <remarks>
        /// This is a good <c>default:</c> case when switching on every possible <see cref="EnumerateDirectoryStatus"/>
        /// </remarks>
        public static Exception ThrowForUnknownError(this EnumerateDirectoryResult result)
        {
            Contract.Requires(result.Status == EnumerateDirectoryStatus.UnknownError);
            throw result.CreateExceptionForError();
        }

        /// <summary>
        /// Throws an exception if the native error code was corresponds to a known <see cref="EnumerateDirectoryStatus"/>
        /// (and enumeration was not successful).
        /// </summary>
        public static Exception ThrowForKnownError(this EnumerateDirectoryResult result)
        {
            Contract.Requires(result.Status != EnumerateDirectoryStatus.UnknownError && result.Status != EnumerateDirectoryStatus.Success);
            throw result.CreateExceptionForError();
        }

        /// <summary>
        /// Creates (but does not throw) an exception for this result. The result must not be successful.
        /// </summary>
        public static Exception CreateExceptionForError(this EnumerateDirectoryResult result)
        {
            Contract.Requires(result.Status != EnumerateDirectoryStatus.Success);
            var errorMessage = I($"Enumerating a directory failed: {result.Status:G}");
            return result.ErrorCause != null
                ? new Exception(errorMessage, result.ErrorCause)
                : new NativeWin32Exception(result.NativeErrorCode);
        }
    }
}
