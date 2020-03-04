// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Represents a result of pushing a file.
    /// </summary>
    /// <remarks>
    /// If <code>Value == true</code> then the file was successfully pushed.
    /// If <code>Value == false</code> then the file was not pushed (it was rejected by the server).
    /// </remarks>
    /// Consider having an enum here and propagate the rejection reason from the server.
    public sealed class PushFileResult : Result<bool>
    {
        /// <nodoc />
        public ContentHash Hash { get; }

        /// <inheritdoc />
        public PushFileResult(ContentHash hash, bool result)
            : base(result)
        {
            Hash = hash;
        }

        /// <inheritdoc />
        public PushFileResult(ContentHash hash, string errorMessage, string diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Hash = hash;
        }

        /// <inheritdoc />
        public PushFileResult(ContentHash hash, Exception exception, string message = null)
            : base(exception, message)
        {
            Hash = hash;
        }

        /// <inheritdoc />
        public PushFileResult(ContentHash hash, ResultBase other, string message = null)
            : base(other, message)
        {
            Hash = hash;
        }

        /// <inheritdoc />
        public PushFileResult(ResultBase other, string message = null)
            : base(other, message)
        {
        }

        /// <nodoc />
        public string GetSuccessOrDiagnostics()
            => this switch
                {
                    { Succeeded: false } => Diagnostics,
                    { Value: true } => "Success",
                    { Value: false } => "Skipped/Rejected",
                };

        /// <nodoc />
        public string GetSuccessOrErrorMessage()
            => this switch
                {
                    { Succeeded: false } => ErrorMessage,
                    { Value: true } => "Success",
                    { Value: false } => "Skipped/Rejected",
                };
    }
}
