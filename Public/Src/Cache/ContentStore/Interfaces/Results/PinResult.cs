// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    ///     Result of the Pin call.
    /// </summary>
    public class PinResult : ResultBase, IEquatable<PinResult>
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
        ///     Gets or sets size of the content.
        /// </summary>
        public long ContentSize { get; set; } = -1;

        /// <summary>
        ///     Gets or sets the last time content was used.
        /// </summary>
        public DateTime LastAccessTime { get; set; } = DateTime.MinValue;

        /// <summary>
        ///     Success singleton.
        /// </summary>
        public static readonly PinResult Success = new PinResult(ResultCode.Success);

        /// <summary>
        ///     Content not found singleton.
        /// </summary>
        public static readonly PinResult ContentNotFound = new PinResult(code: ResultCode.ContentNotFound);

        /// <summary>
        ///     Initializes a new instance of the <see cref="PinResult"/> class.
        /// </summary>
        public PinResult(long contentSize = -1, DateTime? lastAccessTime = null, ResultCode code = ResultCode.Success)
            : base(code == ResultCode.Success ? null : code.ToString(), diagnostics: null)
        {
            ContentSize = contentSize;
            LastAccessTime = lastAccessTime ?? DateTime.MinValue;
            Code = code;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PinResult"/> class.
        /// </summary>
        public PinResult(ResultCode code = ResultCode.Success)
            : base(code == ResultCode.Success ? null : code.ToString(), diagnostics: null)
        {
            Code = code;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PinResult"/> class.
        /// </summary>
        public PinResult(ResultCode code, string errorMessage, string diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Contract.Requires(!string.IsNullOrEmpty(errorMessage));
            Contract.Requires(code != ResultCode.Success, "This constructor should be used for error cases only.");

            Code = code;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PinResult"/> class.
        /// </summary>
        public PinResult(string errorMessage, string diagnostics = null)
            : this(ResultCode.Error, errorMessage, diagnostics)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PinResult"/> class.
        /// </summary>
        public PinResult(Exception exception, string message = null)
            : base(exception, message)
        {
            Code = ResultCode.Error;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PinResult"/> class.
        /// </summary>
        public PinResult(ResultBase other, string message = null)
            : base(other, message)
        {
            Code = ResultCode.Error;
        }

        /// <summary>
        ///     Gets the specific result code for the related call.
        /// </summary>
        public readonly ResultCode Code;

        /// <nodoc />
        public ResultCode ErrorCode => Code;

        /// <inheritdoc />
        public override bool Succeeded => Code == ResultCode.Success;

        /// <inheritdoc />
        public bool Equals(PinResult other)
        {
            return EqualsBase(other) && other != null && Code == other.Code;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is PinResult other && Equals(other);
        }

        /// <summary>
        /// Overloads &amp; operator to behave as AND operator.
        /// </summary>
        public static PinResult operator &(PinResult result1, PinResult result2)
        {
            return result1.Succeeded
                ? result2
                : new PinResult(
                    Merge(result1.ErrorMessage, result2.ErrorMessage, ", "),
                    Merge(result1.Diagnostics, result2.Diagnostics, ", "));
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Code.GetHashCode() ^ (ErrorMessage?.GetHashCode() ?? 0);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            switch (Code)
            {
                case ResultCode.Error:
                    return GetErrorString();
                default:
                    return $"{Code}";
            }
        }

        /// <summary>
        /// Merges two strings.
        /// </summary>
        private static string Merge(string s1, string s2, string separator)
        {
            if (s1 == null)
            {
                return s2;
            }

            if (s2 == null)
            {
                return s1;
            }

            separator = separator ?? string.Empty;

            return $"{s1}{separator}{s2}";
        }
    }
}
