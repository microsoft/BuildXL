// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    ///     Result of the Place call.
    /// </summary>
    public class PlaceFileResult : ResultBase, IEquatable<PlaceFileResult>
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
            Code = code;
            FileSize = fileSize;
            LastAccessTime = lastAccessTime ?? DateTime.MinValue;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PlaceFileResult"/> class.
        /// </summary>
        public PlaceFileResult(ResultCode code, string errorMessage, string diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Contract.Requires(!string.IsNullOrEmpty(errorMessage));
            Code = code;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PlaceFileResult"/> class.
        /// </summary>
        public PlaceFileResult(string errorMessage, string diagnostics = null)
            : this(ResultCode.Error, errorMessage, diagnostics)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PlaceFileResult" /> class.
        /// </summary>
        public PlaceFileResult(Exception exception, string message = null)
            : base(exception, message)
        {
            Code = ResultCode.Error;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PlaceFileResult" /> class.
        /// </summary>
        public PlaceFileResult(ResultBase other, string message = null)
            : base(other, message)
        {
            Code = ResultCode.Error;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PlaceFileResult" /> class.
        /// </summary>
        public PlaceFileResult(ResultBase other, ResultCode code, string message = null)
            : base(other, message)
        {
            Code = code;
        }

        /// <inheritdoc />
        public override bool Succeeded => Code < ResultCode.Error; // TODO: why Code == ResultCode.Unknown is consider as success?

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
        public bool Equals(PlaceFileResult other)
        {
            return EqualsBase(other) && other != null && Code == other.Code;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is PlaceFileResult other && Equals(other);
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
                    return $"{Code} Size={FileSize}{this.GetDiagnosticsMessageForTracing()}";
            }
        }
    }
}
