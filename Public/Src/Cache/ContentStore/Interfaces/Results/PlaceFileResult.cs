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
        /// The source of a successfully materialized file.
        /// </summary>
        public enum Source : byte
        {
            /// <summary>
            /// Default zero value.
            /// </summary>
            Unknown,

            /// <summary>
            /// The file was present in the local cache ("L1").
            /// </summary>
            LocalCache,

            /// <summary>
            /// The file comes from the datacenter peer-to-peer cache ("L2").
            /// </summary>
            DatacenterCache,

            /// <summary>
            /// The file comes from backing store, i.e. Artifact Services or "L3".
            /// </summary>
            BackingStore,

            /// <summary>
            /// The file was obtained from the cold storage.
            /// </summary>
            ColdStorage,
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PlaceFileResult" /> class.
        /// </summary>
        public PlaceFileResult(ResultCode code, long fileSize = 0, DateTime? lastAccessTime = null, Source source = Source.Unknown)
        {
            Contract.Requires(code != ResultCode.Error);

            Code = code;
            FileSize = fileSize;
            LastAccessTime = lastAccessTime ?? DateTime.MinValue;
            MaterializationSource = source;
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

        /// <summary>
        /// Creates a successful result for materialization operation.
        /// </summary>
        public static PlaceFileResult CreateSuccess(ResultCode code, long? fileSize, Source source, DateTime? lastAccessTime = null) =>
            new (code, fileSize ?? 0, lastAccessTime, source);

        /// <nodoc />
        public static PlaceFileResult ContentNotFound { get; } = new (ResultCode.NotPlacedContentNotFound);

        /// <nodoc />
        public static PlaceFileResult CreateContentNotFound(string? errorMessage) => string.IsNullOrEmpty(errorMessage)
            ? ContentNotFound
            : new PlaceFileResult(ResultCode.NotPlacedContentNotFound, errorMessage!);

        /// <nodoc />
        public static PlaceFileResult AlreadyExists { get; } = new (ResultCode.NotPlacedAlreadyExists);

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
        /// Gets the source of a successfully materialized file.
        /// </summary>
        /// <remarks>
        /// The property is set only when the file was successfully materialized.
        /// </remarks>
        public Source MaterializationSource { get; }

        /// <summary>
        /// Creates a copy of the current operation with a given <paramref name="source"/> if <see cref="Code"/> is not <see cref="ResultCode.Error"/>.
        /// </summary>
        public PlaceFileResult WithMaterializationSource(Source source)
        {
            if (Code != ResultCode.Error)
            {
                var result = new PlaceFileResult(Code, FileSize, LastAccessTime, source);

                if (Diagnostics is not null)
                {
                    result.SetDiagnosticsForSuccess(Diagnostics);
                }

            }

            return this;
        }

        /// <summary>
        ///     Gets or sets size, in bytes, of the content.
        /// </summary>
        public long FileSize { get; }

        /// <summary>
        ///     Gets or set the last time the file was accessed locally.
        /// </summary>
        public DateTime LastAccessTime { get; }

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
            return Code is ResultCode.PlacedWithHardLink or ResultCode.PlacedWithCopy or ResultCode.PlacedWithMove;
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
        protected override string GetSuccessString()
        {
            var source = IsPlaced() ? $" Source={MaterializationSource}" : string.Empty;
            return $"{Code} Size={FileSize}{source}{this.GetDiagnosticsMessageForTracing()}";
        }

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
