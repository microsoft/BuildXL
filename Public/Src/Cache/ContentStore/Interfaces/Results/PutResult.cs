// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;

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
        {
            Contract.Requires(contentHash.HashType != HashType.Unknown);

            ContentHash = contentHash;
            ContentSize = contentSize;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PutResult"/> class.
        /// </summary>
        [Obsolete]
        public PutResult(bool succeeded, ContentHash contentHash, string errorMessage, string diagnostics = null)
            : base(succeeded, errorMessage, diagnostics)
        {
            ContentHash = contentHash;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PutResult"/> class.
        /// </summary>
        public PutResult(ContentHash contentHash, string errorMessage, string diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            ContentHash = contentHash;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PutResult" /> class.
        /// </summary>
        public PutResult(Exception exception, ContentHash contentHash, string message = null)
            : base(exception, message)
        {
            ContentHash = contentHash;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PutResult" /> class.
        /// </summary>
        public PutResult(ResultBase other, ContentHash contentHash, string message = null)
            : base(other, message)
        {
            ContentHash = contentHash;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PutResult" /> class.
        /// </summary>
        public PutResult(ResultBase other, string message = null)
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

        /// <inheritdoc />
        public bool Equals(PutResult other)
        {
            return
                base.Equals(other)
                && other != null
                && ContentHash == other.ContentHash
                && ContentSize == other.ContentSize;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is PutResult other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return base.GetHashCode() ^ ContentHash.GetHashCode() ^ ContentSize.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Succeeded
                ? $"Success Hash={ContentHash.ToShortString()} Size={ContentSize}{this.GetDiagnosticsMessageForTracing()}"
                : GetErrorString();
        }

        internal class ExtraMetadata
        {
            public TimeSpan GateWaitTime;
            public int GateOccupiedCount;
        }

        internal ExtraMetadata Metadata { get; set; }
    }
}
