// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    ///     Result of the Put call.
    /// </summary>
    public class PutResult : BoolResult, IEquatable<PutResult>
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="PutResult" /> class.
        /// </summary>
        public PutResult(ContentHash contentHash, long contentSize)
            : this(contentHash, contentSize, contentAlreadyExistsInCache: false)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PutResult" /> class.
        /// </summary>
        public PutResult(ContentHash contentHash, long contentSize, bool contentAlreadyExistsInCache)
        {
            Contract.Requires(contentHash.HashType != HashType.Unknown);

            ContentHash = contentHash;
            ContentSize = contentSize;
            ContentAlreadyExistsInCache = contentAlreadyExistsInCache;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PutResult"/> class.
        /// </summary>
        public PutResult(ContentHash contentHash, string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            ContentHash = contentHash;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PutResult" /> class.
        /// </summary>
        public PutResult(Exception exception, ContentHash contentHash, string? message = null)
            : base(exception, message)
        {
            ContentHash = contentHash;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PutResult" /> class.
        /// </summary>
        public PutResult(ResultBase other, ContentHash contentHash, string? message = null)
            : base(other, message)
        {
            ContentHash = contentHash;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PutResult" /> class.
        /// </summary>
        public PutResult(ResultBase other, string? message = null)
            : base(other, message)
        {
        }

        /// <summary>
        ///     Gets hash of the content.
        /// </summary>
        public readonly ContentHash ContentHash;

        /// <summary>
        ///     Gets size, in bytes, of the content.
        /// </summary>
        public readonly long ContentSize;

        /// <summary>
        /// Whether the content existed in the cache prior to this put.
        /// </summary>
        public readonly bool ContentAlreadyExistsInCache;

        /// <inheritdoc />
        public bool Equals([AllowNull]PutResult other)
        {
            return
                base.Equals(other)
                && other != null
                && ContentHash == other.ContentHash
                && ContentSize == other.ContentSize
                && ContentAlreadyExistsInCache == other.ContentAlreadyExistsInCache;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is PutResult other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return base.GetHashCode() ^ ContentHash.GetHashCode() ^ ContentSize.GetHashCode() ^ ContentAlreadyExistsInCache.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Succeeded
                ? $"Success Hash={ContentHash.ToShortString()} Size={ContentSize} {nameof(ContentAlreadyExistsInCache)}={ContentAlreadyExistsInCache}{this.GetDiagnosticsMessageForTracing()}"
                : GetErrorString();
        }

        internal ResultMetaData? MetaData { get; set; }
    }
}
