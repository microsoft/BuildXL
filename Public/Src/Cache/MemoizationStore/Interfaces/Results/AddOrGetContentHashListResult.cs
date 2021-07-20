// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Interfaces.Results
{
    /// <summary>
    ///     Result of the Add call.
    /// </summary>
    public class AddOrGetContentHashListResult : ResultBase, IEquatable<AddOrGetContentHashListResult>
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
            ///     Something failed
            /// </summary>
            Error,

            /// <summary>
            ///     Cache was asked to replace metadata with mixed SinglePhaseNonDeterministic records.
            /// </summary>
            SinglePhaseMixingError,

            /// <summary>
            ///     Cache was asked to replace a ToolDeterministic record with a different ToolDeterministic record.
            /// </summary>
            InvalidToolDeterminismError
        }

        /// <summary>
        ///     SinglePhaseMixingError singleton.
        /// </summary>
        public static readonly AddOrGetContentHashListResult SinglePhaseMixingError =
            new AddOrGetContentHashListResult(ResultCode.SinglePhaseMixingError, default(ContentHashListWithDeterminism));

        /// <summary>
        ///     Initializes a new instance of the <see cref="AddOrGetContentHashListResult"/> class.
        /// </summary>
        public AddOrGetContentHashListResult(
            ResultCode code,
            ContentHashListWithDeterminism contentHashListWithDeterminism)
        {
            Code = code;
            ContentHashListWithDeterminism = contentHashListWithDeterminism;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="AddOrGetContentHashListResult" /> class.
        /// </summary>
        public AddOrGetContentHashListResult(ContentHashListWithDeterminism contentHashListWithDeterminism)
            : this(ResultCode.Success, contentHashListWithDeterminism)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="AddOrGetContentHashListResult" /> class.
        /// </summary>
        public AddOrGetContentHashListResult(string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Contract.Requires(!string.IsNullOrEmpty(errorMessage));
            Code = ResultCode.Error;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="AddOrGetContentHashListResult"/> class.
        /// </summary>
        public AddOrGetContentHashListResult(Exception exception)
            : base(exception)
        {
            Code = ResultCode.Error;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="AddOrGetContentHashListResult" /> class.
        /// </summary>
        public AddOrGetContentHashListResult(ResultBase other, string? message = null)
            : base(other, message)
        {
            Code = ResultCode.Error;
        }

        /// <inheritdoc />
        public override Error? Error
        {
            get
            {
                return Code == ResultCode.Success
                    ? null
                    : (base.Error ?? Error.FromErrorMessage(Code.ToString()));
            }
        }

        /// <summary>
        ///     Gets the specific result code for the related call.
        /// </summary>
        public ResultCode Code { get; }

        /// <summary>
        ///     Gets the resulting stored value.
        /// </summary>
        /// <remarks>
        ///     Contains a null ContentHashList if the given value was accepted (either won the race or was equivalent to the winner).
        ///     In either case, contains the determinism guarantee associated with the winning value.
        /// </remarks>
        public readonly ContentHashListWithDeterminism ContentHashListWithDeterminism;

        /// <inheritdoc />
        public bool Equals(AddOrGetContentHashListResult? other)
        {
            if (other == null || Code != other.Code)
            {
                return false;
            }

            return ContentHashListWithDeterminism.Equals(other.ContentHashListWithDeterminism);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is AddOrGetContentHashListResult other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (base.GetHashCode(), Code, ContentHashListWithDeterminism).GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (!Succeeded)
            {
                return $"{Code} {GetErrorString()}";
            }

            var addOrGet = ContentHashListWithDeterminism.ContentHashList != null ? "get" : "add";
            return $"{Code} {addOrGet} ContentHashList=[{ContentHashListWithDeterminism.ContentHashList}], Determinism=[{ContentHashListWithDeterminism.Determinism}]]";
        }
    }
}
