// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    ///     Result of the OpenStream call.
    /// </summary>
    public class OpenStreamResult : ResultBase
    {
        /// <summary>
        ///     A code that helps caller to make decisions.
        /// </summary>
        public enum ResultCode
        {
            /// <summary>
            ///     The call succeeded
            /// </summary>
            Success = 0,

            /// <summary>
            ///     An error occurred, see ErrorMessage for description.
            /// </summary>
            Error,

            /// <summary>
            ///     Content was not found.
            /// </summary>
            ContentNotFound
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="OpenStreamResult" /> class with resulting stream.
        /// </summary>
        public OpenStreamResult(StreamWithLength? stream)
        {
            Code = stream != null ? ResultCode.Success : ResultCode.ContentNotFound;
            StreamWithLength = stream;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="OpenStreamResult"/> class.
        /// </summary>
        public OpenStreamResult(ResultCode code, string? errorMessage, string? diagnostics = null)
            : base(errorMessage ?? code.ToString(), diagnostics)
        {
            Contract.Requires(code != ResultCode.Success);
            Code = code;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="OpenStreamResult"/> class.
        /// </summary>
        public OpenStreamResult(string errorMessage, string? diagnostics = null)
            : this(ResultCode.Error, errorMessage, diagnostics)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="OpenStreamResult" /> class.
        /// </summary>
        public OpenStreamResult(Exception exception, string? message = null)
            : base(exception, message)
        {
            Code = ResultCode.Error;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="OpenStreamResult" /> class.
        /// </summary>
        public OpenStreamResult(ResultBase other, string? message = null)
            : base(other, message)
        {
            Code = ResultCode.Error;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="OpenStreamResult" /> class.
        /// </summary>
        public OpenStreamResult(ResultBase other, ResultCode code, string? message = null)
            : base(other, message)
        {
            Contract.Requires(code != ResultCode.Success);
            Code = code;
        }

        /// <inheritdoc />
        public override Error? Error
        {
            // Need to override this property to maintain the invariant: !Success => Error != null
            get
            {
                return Code == ResultCode.Success ? null : (base.Error ?? Error.FromErrorMessage(Code.ToString()));
            }
        }

        /// <summary>
        ///     Gets the specific result code for the related call.
        /// </summary>
        public readonly ResultCode Code;

        /// <summary>
        ///     Gets opened stream.
        /// </summary>
        public Stream? Stream => StreamWithLength?.Stream;

        /// <summary>
        ///     Gets opened stream.
        /// </summary>
        public readonly StreamWithLength? StreamWithLength;

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (Code, Stream == null, base.GetHashCode()).GetHashCode();
        }

        /// <inheritdoc />
        protected override bool SuccessEquals(ResultBase other)
        {
            var rhs = (OpenStreamResult)other;
            return Code == rhs.Code && Stream == rhs.Stream;
        }

        /// <inheritdoc />
        protected override string GetSuccessString() => $"{Code} Size={Stream?.Length}{this.GetDiagnosticsMessageForTracing()}";

        /// <inheritdoc />
        public override string ToString()
        {
            switch (Code)
            {
                case ResultCode.Error:
                    return GetErrorString();
                default:
                    return $"{Code} Size={Stream?.Length}{this.GetDiagnosticsMessageForTracing()}";
            }
        }
    }
}
