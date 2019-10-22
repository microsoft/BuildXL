// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
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
        public DeleteResult(ResultCode resultCode, ContentHash contentHash, long evictedSize, long pinnedSize)
            : base(IsSuccessfulResult(resultCode))
        {
            Code = resultCode;
            ContentHash = contentHash;
            EvictedSize = evictedSize;
            PinnedSize = pinnedSize;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DeleteResult"/> class.
        /// </summary>
        public DeleteResult(ResultCode resultCode, string errorMessage, string diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Code = resultCode;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DeleteResult"/> class.
        /// </summary>
        public DeleteResult(ResultCode resultCode, Exception exception, string message = null)
            : base(exception, message)
        {
            Code = resultCode;
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
        public long EvictedSize { get; }

        /// <summary>
        ///     Gets byte count remaining pinned across all replicas for associated content hash.
        /// </summary>
        public long PinnedSize { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return Succeeded
                ? $"Success Code={Code} Hash={ContentHash} Size={EvictedSize} Pinned={PinnedSize}"
                : GetErrorString();
        }
    }
}
