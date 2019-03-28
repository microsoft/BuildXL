// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Results
{
    /// <summary>
    ///     Result of the GetSelector call.
    /// </summary>
    public class GetSelectorResult : BoolResult, IEquatable<GetSelectorResult>
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="GetSelectorResult"/> class.
        /// </summary>
        public GetSelectorResult(Selector selector)
        {
            Selector = selector;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="GetSelectorResult"/> class.
        /// </summary>
        public GetSelectorResult(string errorMessage, string diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Contract.Requires(errorMessage != null);
            Contract.Requires(errorMessage.Length > 0);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="GetSelectorResult"/> class.
        /// </summary>
        public GetSelectorResult(Exception exception)
            : base(exception)
        {
            Contract.Requires(exception != null);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="GetSelectorResult"/> class.
        /// </summary>
        public GetSelectorResult(ResultBase other, string message = null)
            : base(other, message)
        {
            Contract.Requires(other != null);
        }

        /// <summary>
        ///     Gets the retrieved selector.
        /// </summary>
        public readonly Selector Selector;

        /// <inheritdoc />
        public bool Equals(GetSelectorResult other)
        {
            return base.Equals(other) && other != null && Selector == other.Selector;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is GetSelectorResult other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return base.GetHashCode() ^ Selector.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Succeeded ? $"{Selector}" : GetErrorString();
        }
    }
}
