// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Results
{
    /// <summary>
    /// Indicates which cache level a <see cref="GetSelectorResult"/> originated from.
    /// </summary>
    public enum SelectorSourceCacheLevel
    {
        /// <summary>
        /// The selector comes from a local cache.
        /// </summary>
        Local,

        /// <summary>
        /// The selector comes from a remote cache.
        /// </summary>
        Remote,
    }

    /// <summary>
    ///     Result of the GetSelector call.
    /// </summary>
    public class GetSelectorResult : BoolResult, IEquatable<GetSelectorResult>
    {
        /// <summary>
        /// Optional. When set, indicates the cache level from which this selector originates.
        /// The convention is that producers only set this on the *first* selector of a
        /// contiguous run of selectors from the same level (so consumers can detect level
        /// transitions). Consumers that don't care about cache-level provenance can safely
        /// ignore this property.
        /// </summary>
        /// <remarks>
        /// Null means "unspecified" — consumers should treat the selector as belonging to
        /// the previously declared level (defaulting to <see cref="SelectorSourceCacheLevel.Local"/>
        /// if no level has been declared yet).
        /// </remarks>
        public SelectorSourceCacheLevel? SourceCacheLevel { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="GetSelectorResult"/> class.
        /// </summary>
        public GetSelectorResult(Selector selector)
        {
            Selector = selector;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="GetSelectorResult"/> class with a cache-level tag.
        /// </summary>
        public GetSelectorResult(Selector selector, SelectorSourceCacheLevel sourceCacheLevel)
        {
            Selector = selector;
            SourceCacheLevel = sourceCacheLevel;
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
            return base.Equals(other) && other != null && Selector == other.Selector && SourceCacheLevel == other.SourceCacheLevel;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is GetSelectorResult other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return base.GetHashCode() ^ Selector.GetHashCode() ^ (SourceCacheLevel?.GetHashCode() ?? 0);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (!Succeeded)
            {
                return GetErrorString();
            }

            return SourceCacheLevel.HasValue ? $"{Selector} [{SourceCacheLevel.Value}]" : $"{Selector}";
        }
    }
}
