// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities.Collections;

namespace BuildXL.Engine.Cache.Artifacts
{
    /// <summary>
    /// Batch result for <see cref="IArtifactContentCache.TryLoadAvailableContentAsync"/>.
    /// This is a list of <c>(ContentHash, IsAvailable)</c> pairs with an aggregate <see cref="AllContentAvailable"/> flag
    /// (equivalent to AND-ind all the per-hash availability flags).
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct ContentAvailabilityBatchResult
    {
        /// <summary>
        /// Indicates if each item has <c><see cref="ContentAvailabilityResult.IsAvailable"/> == true</c>
        /// </summary>
        public readonly bool AllContentAvailable;

        /// <summary>
        /// Per-hash results. Some or all results may indicate unavailability.
        /// </summary>
        public readonly ReadOnlyArray<ContentAvailabilityResult> Results;

        /// <nodoc />
        public ContentAvailabilityBatchResult(ReadOnlyArray<ContentAvailabilityResult> results, bool allContentAvailable)
        {
            AllContentAvailable = allContentAvailable;
            Results = results;
        }
    }
}
