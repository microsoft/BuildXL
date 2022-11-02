// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Results
{
    /// <summary>
    /// A source of content hash list.
    /// </summary>
    public enum ContentHashListSource
    {
        /// <summary>
        /// The source is unknown.
        /// </summary>
        Unknown,
        
        /// <summary>
        /// A content hash list was obtained from a global store.
        /// </summary>
        Shared,

        /// <summary>
        /// A content hash list was obtained from a local database.
        /// </summary>
        Local,
    }
    
    /// <summary>
    ///     Result of the Get call
    /// </summary>
    public class GetContentHashListResult : BoolResult, IEquatable<GetContentHashListResult>
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="GetContentHashListResult"/> class.
        /// </summary>
        public GetContentHashListResult(ContentHashListWithDeterminism contentHashListWithDeterminism, ContentHashListSource source = ContentHashListSource.Unknown)
        {
            Source = source;
            ContentHashListWithDeterminism = contentHashListWithDeterminism;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="GetContentHashListResult"/> class.
        /// </summary>
        public GetContentHashListResult(string errorMessage, string diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Contract.Requires(errorMessage != null);
            Contract.Requires(errorMessage.Length > 0);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="GetContentHashListResult"/> class.
        /// </summary>
        public GetContentHashListResult(Exception exception)
            : base(exception)
        {
            Contract.Requires(exception != null);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="GetContentHashListResult"/> class.
        /// </summary>
        public GetContentHashListResult(ResultBase other, string message = null)
            : base(other, message)
        {
            Contract.Requires(other != null);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="GetContentHashListResult"/> class.
        /// </summary>
        public GetContentHashListResult(ResultBase other, ContentHashListSource source)
            : base(other, message: null)
        {
            Source = source;
        }

        /// <summary>
        ///     Gets the resulting stored value.
        /// </summary>
        /// <remarks>
        ///     Contains a null ContentHashList on a miss.
        ///     Also contains the determinism guarantee, if any, associated with the value.
        /// </remarks>
        public readonly ContentHashListWithDeterminism ContentHashListWithDeterminism;

        /// <summary>
        ///     Gets the source of the result.
        /// </summary>
        public readonly ContentHashListSource Source;

        /// <summary>
        ///     Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>true if the current object is equal to the other parameter; otherwise, false.</returns>
        public bool Equals(GetContentHashListResult other)
        {
            if (!base.Equals(other) || other == null)
            {
                return false;
            }

            return ContentHashListWithDeterminism.Equals(other.ContentHashListWithDeterminism);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is GetContentHashListResult other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return base.GetHashCode() ^ ContentHashListWithDeterminism.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (!Succeeded)
            {
                return GetErrorString();
            }

            var hitOrMiss = ContentHashListWithDeterminism.ContentHashList != null ? "hit" : "miss";
            var result = $"Success {hitOrMiss} Determinism=[{ContentHashListWithDeterminism.Determinism}]";

            if (Source != ContentHashListSource.Unknown)
            {
                result += $", Source=[{Source}]";
            }
            
            return result;
        }

        /// <inheritdoc />
        protected override string GetSuccessString()
        {
            var contentHashes = ContentHashListWithDeterminism.ContentHashList.Hashes;
            return $"{base.GetSuccessString()} Count={contentHashes.Count}" + (contentHashes.Count != 0 ? $" firstHash={contentHashes[0]}" : string.Empty);
        }
    }
}
