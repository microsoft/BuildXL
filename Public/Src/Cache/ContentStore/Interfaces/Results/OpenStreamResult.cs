// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    ///     Result of the OpenStream call.
    /// </summary>
    public class OpenStreamResult : ResultBase, IEquatable<OpenStreamResult>
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
        public OpenStreamResult(Stream stream)
        {
            Code = stream != null ? ResultCode.Success : ResultCode.ContentNotFound;
            Stream = stream;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="OpenStreamResult"/> class.
        /// </summary>
        public OpenStreamResult(ResultCode code, string errorMessage, string diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Code = code;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="OpenStreamResult"/> class.
        /// </summary>
        public OpenStreamResult(string errorMessage, string diagnostics = null)
            : this(ResultCode.Error, errorMessage, diagnostics)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="OpenStreamResult" /> class.
        /// </summary>
        public OpenStreamResult(Exception exception, string message = null)
            : base(exception, message)
        {
            Code = ResultCode.Error;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="OpenStreamResult" /> class.
        /// </summary>
        public OpenStreamResult(ResultBase other, string message = null)
            : base(other, message)
        {
            Code = ResultCode.Error;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="OpenStreamResult" /> class.
        /// </summary>
        public OpenStreamResult(ResultBase other, ResultCode code, string message = null)
            : base(other, message)
        {
            Code = code;
        }

        /// <inheritdoc />
        public override bool Succeeded => Code == ResultCode.Success;

        /// <summary>
        ///     Gets the specific result code for the related call.
        /// </summary>
        public readonly ResultCode Code;

        /// <summary>
        ///     Gets opened stream.
        /// </summary>
        public readonly Stream Stream;

        /// <inheritdoc />
        public bool Equals(OpenStreamResult other)
        {
            return EqualsBase(other) && other != null && Code == other.Code;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is OpenStreamResult other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Code.GetHashCode() ^ (Stream == null).GetHashCode() ^ (ErrorMessage?.GetHashCode() ?? 0);
        }

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
