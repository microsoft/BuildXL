// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    ///     Result of the Pin call.
    /// </summary>
    public class PinResult : ResultBase
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
        {
            ContentSize = contentSize;
            LastAccessTime = lastAccessTime ?? DateTime.MinValue;
            Code = code;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PinResult"/> class.
        /// </summary>
        public PinResult(ResultCode code = ResultCode.Success)
        {
            Code = code;
        }

        /// <nodoc />
        public PinResult(ResultCode code, Error error)
            : base(error)
        {
            Contract.Requires(code != ResultCode.Success, "This constructor should be used for error cases only.");
            Code = code;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PinResult"/> class.
        /// </summary>
        public PinResult(ResultCode code, string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Contract.RequiresNotNullOrEmpty(errorMessage);
            Contract.Requires(code != ResultCode.Success, "This constructor should be used for error cases only.");
            Code = code;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PinResult"/> class.
        /// </summary>
        public PinResult(string errorMessage, string? diagnostics = null)
            : this(ResultCode.Error, errorMessage, diagnostics)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PinResult"/> class.
        /// </summary>
        public PinResult(Exception exception, string? message = null)
            : base(exception, message)
        {
            Code = ResultCode.Error;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PinResult"/> class.
        /// </summary>
        public PinResult(ResultBase other, string? message = null)
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
        public override Error? Error
        {
            // Need to override this property to maintain the invariant: !Success => Error != null
            get
            {
                return Code == ResultCode.Success ? null : (base.Error ?? Error.FromErrorMessage(Code.ToString()));
            }
        }

        /// <inheritdoc />
        protected override bool SuccessEquals(ResultBase other)
        {
            return Code == ((PinResult)other).Code;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (Code, base.GetHashCode()).GetHashCode();
        }

        /// <summary>
        /// Overloads &amp; operator to behave as AND operator.
        /// </summary>
        public static PinResult operator &(PinResult left, PinResult right)
        {
            if (left.Succeeded)
            {
                return right;
            }

            if (right.Succeeded)
            {
                return left;
            }

            // We can't merge the codes, so when both results are failures, we pick the code from the first result.
            return MergeFailures(left, right, () => new PinResult(left.Code), error => new PinResult(left.Code, error));
        }
    }
}
