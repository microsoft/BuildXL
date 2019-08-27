// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Represents a result for File Existence checks.
    /// </summary>
    public class FileExistenceResult : BoolResult, IEquatable<FileExistenceResult>
    {
        /// <summary>
        /// A code that helps caller to make decisions.
        /// </summary>
        public enum ResultCode
        {
            /// <summary>
            /// The call succeeded
            /// </summary>
            FileExists = 0,

            /// <summary>
            /// The cause of the exception the source file not being found.
            /// </summary>
            FileNotFound = 1,

            /// <summary>
            /// File Existence check timed out
            /// </summary>
            Timeout = 2,

            /// <summary>
            /// The cause of the exception was the destination machine being in an error state.
            /// </summary>
            Error = 3,

            /// <summary>
            /// The cause of the exception was the source machine being in an error state.
            /// </summary>
            SourceError = 4
        }

        /// <summary>
        /// Gets the result for the file existence.
        /// </summary>
        public readonly ResultCode Code;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileExistenceResult" /> class.
        /// </summary>
        public FileExistenceResult(ResultCode code = ResultCode.FileExists)
            : base(code != ResultCode.Error)
        {
            Code = code;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileExistenceResult" /> class.
        /// </summary>
        public FileExistenceResult(ResultCode code, string message, string diagnostics = null)
            : base(message, diagnostics)
        {
            Code = code;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileExistenceResult" /> class.
        /// </summary>
        public FileExistenceResult(ResultCode code, Exception innerException, string message = null)
            : base(innerException, message)
        {
            Code = code;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileExistenceResult" /> class.
        /// </summary>
        public FileExistenceResult(ResultCode code, ResultBase other, string message = null)
            : base(other, message)
        {
            Code = code;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileExistenceResult" /> class.
        /// </summary>
        public FileExistenceResult(ResultBase other, string message = null)
            : base(other, message)
        {
            Code = ResultCode.Error;
        }

        /// <summary>
        /// Returns true if the file exists.
        /// </summary>
        public bool Exists => Code == ResultCode.FileExists;

        /// <inheritdoc />
        public bool Equals(FileExistenceResult other)
        {
            return EqualsBase(other) && other != null && Code == other.Code;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is CopyFileResult other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Code.GetHashCode() ^ (ErrorMessage?.GetHashCode() ?? 0);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Exists ? Code.ToString() : $"{Code}: {GetErrorString()}";
        }
    }
}
