// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    ///     Result of the Delete call.
    /// </summary>
    public class DeleteResult : BoolResult
    {
        private static bool IsSuccessfulResult(ResultCode code)
        {
            switch (code)
            {
                case ResultCode.ContentNotFound:
                case ResultCode.Success:
                    return true;
                case ResultCode.ContentNotDeleted:
                case ResultCode.ServerError:
                case ResultCode.Error:
                    return false;
                default:
                    throw new ArgumentException($"{code} is an unrecognized value of {nameof(DeleteResult)}.{nameof(ResultCode)}");
            }
        }

        /// <summary>
        /// A code that helps caller to make decisions.
        /// </summary>
        public enum ResultCode
        {
            /// <summary>
            /// The content does not exist on the server.
            /// This deletion is successful.
            /// </summary>
            ContentNotFound = 0,

            /// <summary>
            /// The content was found and deleted.
            /// </summary>
            Success = 1,

            /// <summary>
            /// Deletion of the content failed.
            /// This deletion is an error.
            /// </summary>
            ContentNotDeleted = 2,

            /// <summary>
            /// The server threw an exception.
            /// </summary>
            ServerError = 3,

            /// <summary>
            /// The cause of the exception was unknown.
            /// </summary>
            Error = 4
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DeleteResult"/> class.
        /// </summary>
        public DeleteResult(ResultCode resultCode, ContentHash contentHash, long contentSize)
        {
            Code = resultCode;
            ContentHash = contentHash;
            ContentSize = contentSize;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DeleteResult"/> class.
        /// </summary>
        public DeleteResult(ResultCode resultCode, string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Contract.Requires(!IsSuccessfulResult(resultCode));
            Code = resultCode;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DeleteResult"/> class.
        /// </summary>
        public DeleteResult(ResultCode resultCode, Exception exception, string? message = null)
            : base(exception, message)
        {
            Contract.Requires(!IsSuccessfulResult(resultCode));
            Code = resultCode;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DeleteResult"/> class.
        /// </summary>
        public DeleteResult(ContentHash contentHash, long contentSize)
            : this(ResultCode.Success, contentHash, contentSize)
        {
        }

        /// <nodoc />
        public DeleteResult(ResultBase other, string message)
            : base(other, message)
        {
            Code = ResultCode.Error;
        }

        /// <summary>
        /// Gets a classification of the result of the call.
        /// </summary>
        public ResultCode Code { get; }

        /// <summary>
        ///     Gets the deleted hash.
        /// </summary>
        public ContentHash ContentHash { get; }

        /// <summary>
        ///     Gets number of bytes evicted.
        /// </summary>
        public long ContentSize { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return Succeeded
                ? $"Status={Code}"
                : GetErrorString();
        }

        /// <inheritdoc />
        public override Error? Error
        {
            get
            {
                return IsSuccessfulResult(Code) ? null : (base.Error ?? Error.FromErrorMessage(Code.ToString()));
            }
        }
    }
}
