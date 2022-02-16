// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Engine.Cache.Fingerprints.TwoPhase
{
    /// <summary>
    /// Options for <see cref="ITwoPhaseFingerprintStore.TryPublishCacheEntryAsync"/>
    /// </summary>
    public readonly record struct PublishCacheEntryOptions
    {
        /// <summary>
        /// Options with <see cref="ShouldPublishAssociatedContent"/> set to true.
        /// </summary>
        public static PublishCacheEntryOptions PublishAssociatedContent { get; } = new PublishCacheEntryOptions() with { ShouldPublishAssociatedContent = true };

        /// <summary>
        /// Indicates associated content should be published along with cache entry if not already published on when stored.
        /// </summary>
        public bool ShouldPublishAssociatedContent { get; init; }
    }
}
