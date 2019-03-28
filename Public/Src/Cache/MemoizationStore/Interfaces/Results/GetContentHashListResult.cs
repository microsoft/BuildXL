// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Results
{
    /// <summary>
    ///     Result of the Get call
    /// </summary>
    public class GetContentHashListResult : BoolResult, IEquatable<GetContentHashListResult>
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="GetContentHashListResult"/> class.
        /// </summary>
        public GetContentHashListResult(ContentHashListWithDeterminism contentHashListWithDeterminism)
        {
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
        ///     Gets the resulting stored value.
        /// </summary>
        /// <remarks>
        ///     Contains a null ContentHashList on a miss.
        ///     Also contains the determinism guarantee, if any, associated with the value.
        /// </remarks>
        public readonly ContentHashListWithDeterminism ContentHashListWithDeterminism;

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
            return $"Success {hitOrMiss} Determinism=[{ContentHashListWithDeterminism.Determinism}]";
        }
    }
}
