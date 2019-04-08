// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Native.IO;

namespace BuildXL.Native.Streams
{
    /// <summary>
    /// Result of a pending or completed I/O operation.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct FileAsyncIOResult
    {
        /// <summary>
        /// Success / completion status. The rest of the result is not valid when the status is <see cref="FileAsyncIOStatus.Pending"/>.
        /// </summary>
        public readonly FileAsyncIOStatus Status;
        private readonly int m_bytesTransferred;
        private readonly int m_error;

        internal FileAsyncIOResult(FileAsyncIOStatus status, int bytesTransferred, int error)
        {
            Contract.Requires(bytesTransferred >= 0);
            Contract.Requires((status == FileAsyncIOStatus.Succeeded) == (error == NativeIOConstants.ErrorSuccess));

            Status = status;
            m_bytesTransferred = bytesTransferred;
            m_error = error;
        }

        /// <summary>
        /// Number of bytes transferred (from the requested start offset).
        /// Present only when the result status is not <see cref="FileAsyncIOStatus.Pending"/>.
        /// </summary>
        public int BytesTransferred
        {
            get
            {
                Contract.Requires(Status != FileAsyncIOStatus.Pending);
                return m_bytesTransferred;
            }
        }

        /// <summary>
        /// Native error code.
        /// Present only when the result status is not <see cref="FileAsyncIOStatus.Pending"/>.
        /// If the status is <see cref="FileAsyncIOStatus.Succeeded"/>, then this is <c>ERROR_SUCCESS</c>.
        /// </summary>
        public int Error
        {
            get
            {
                Contract.Requires(Status != FileAsyncIOStatus.Pending);
                return m_error;
            }
        }

        /// <summary>
        /// Indicates if the native error code specifies that the end of the file has been reached (specific to reading).
        /// Present only when the result status is not <see cref="FileAsyncIOStatus.Pending"/>.
        /// </summary>
        public bool ErrorIndicatesEndOfFile
        {
            get
            {
                return Error == NativeIOConstants.ErrorHandleEof;
            }
        }
    }
}
