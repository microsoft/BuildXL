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

        /// <summary>
        /// A code that helps caller to make decisions.
        /// </summary>
        public enum ResultCode
        {
            /// <summary>
            /// The call succeeded.
            /// </summary>
            Success = 0,

            /// <summary>
            /// The call did not succeed on the server.
            /// </summary>
            ContentNotDeleted = 1,

            /// <summary>
            /// The cause of the exception was the server.
            /// </summary>
            ServerError = 2,

            /// <summary>
            /// The cause of the exception was unknown.
            /// </summary>
            Error = 3
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DeleteResult"/> class.
        /// </summary>
        public DeleteResult(ContentHash contentHash, long evictedSize, long pinnedSize)
        {
            Code = ResultCode.Success;
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
                ? $"Success Hash={ContentHash} Size={EvictedSize} Pinned={PinnedSize}"
                : GetErrorString();
        }
    }
}
