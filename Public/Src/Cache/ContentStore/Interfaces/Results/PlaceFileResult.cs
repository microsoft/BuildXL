// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    ///     Result of the Place call.
    /// </summary>
    public class PlaceFileResult : ResultBase
    {
        /// <summary>
        ///     A code that informs the caller what happened.
        /// </summary>
        public enum ResultCode : byte
        {
            /// <summary>
            ///     Still unknown.
            /// </summary>
            Unknown = 0,

            /// <summary>
            ///     Content was placed into a file via hard link.
            /// </summary>
            PlacedWithHardLink = 1,

            /// <summary>
            ///     Content was placed into a file via copy.
            /// </summary>
            PlacedWithCopy = 2,

            /// <summary>
            ///     Content was placed into a file via move.
            /// </summary>
            PlacedWithMove = 3,

            /// <summary>
            ///     An error occurred, see ErrorMessage for description.
            /// </summary>
            Error = 100,

            /// <summary>
            ///     Content was not found.
            /// </summary>
            NotPlacedContentNotFound = 101,

            /// <summary>
            ///     Destination file already exists.
            /// </summary>
            NotPlacedAlreadyExists = 102
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PlaceFileResult" /> class.
        /// </summary>
        public PlaceFileResult(ResultCode code, long fileSize = 0, DateTime? lastAccessTime = null)
        {
            Contract.Requires(code != ResultCode.Error);

            Code = code;
            FileSize = fileSize;
            LastAccessTime = lastAccessTime ?? DateTime.MinValue;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PlaceFileResult"/> class.
        /// </summary>
        public PlaceFileResult(ResultCode code, string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Contract.Requires(code >= ResultCode.Error);
            Contract.RequiresNotNullOrEmpty(errorMessage);
            Code = code;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PlaceFileResult"/> class.
        /// </summary>
        public PlaceFileResult(string errorMessage, string? diagnostics = null)
            : this(ResultCode.Error, errorMessage, diagnostics)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PlaceFileResult" /> class.
        /// </summary>
        public PlaceFileResult(Exception exception, string? message = null)
            : base(exception, message)
        {
            Code = ResultCode.Error;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PlaceFileResult" /> class.
        /// </summary>
        public PlaceFileResult(ResultBase other, string? message = null)
            : base(other, message)
        {
            Code = ResultCode.Error;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PlaceFileResult" /> class.
        /// </summary>
        public PlaceFileResult(ResultBase other, ResultCode code, string? message = null)
            : base(other, message)
        {
            Contract.Requires(code >= ResultCode.Error);
            Code = code;
        }

        /// <inheritdoc />
        public override Error? Error
        {
            // Need to override this property to maintain the invariant: !Success => Error != null
            get
            {
                // TODO: why Code == ResultCode.Unknown is consider as success?
                return Code < ResultCode.Error ? null : (base.Error ?? Error.FromErrorMessage(Code.ToString()));
            }
        }

        /// <summary>
        ///     Gets the specific result code for the related call.
        /// </summary>
        public readonly ResultCode Code;

        /// <summary>
        ///     Gets or sets size, in bytes, of the content.
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        ///     Gets or set the last time the file was accessed locally.
        /// </summary>
        public DateTime LastAccessTime { get; set; }

        /// <summary>
        /// An optional additional information associated with the result.
        /// </summary>
        internal ResultMetaData? Metadata { get; set; }

        /// <summary>
        /// Implicit conversion operator from <see cref="PlaceFileResult"/> to <see cref="bool"/>.
        /// </summary>
        public static implicit operator bool(PlaceFileResult result) => result != null && result.Succeeded;

        /// <summary>
        ///     True if file was place with any method.
        /// </summary>
        public bool IsPlaced()
        {
            return Code == ResultCode.PlacedWithHardLink || Code == ResultCode.PlacedWithCopy || Code == ResultCode.PlacedWithMove;
        }

        /// <inheritdoc />
        protected override bool SuccessEquals(ResultBase other)
        {
            return Code == ((PlaceFileResult)other).Code;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (Code, base.GetHashCode()).GetHashCode();
        }

        /// <inheritdoc />
        protected override string GetSuccessString() => $"{Code} Size={FileSize}{this.GetDiagnosticsMessageForTracing()}";

        /// <inheritdoc />
        protected override string GetErrorString()
        {
            if (Error is not null && Code != ResultCode.Error)
            {
                // Its possible that the operation failed but the code is not error.
                // For instance, the copy may fail and the resulting error code in some cases can be 'NotPlacedContentNotFound'.
                return $"{Code} {Error}";
            }

            return base.GetErrorString();
        }
    }
}
