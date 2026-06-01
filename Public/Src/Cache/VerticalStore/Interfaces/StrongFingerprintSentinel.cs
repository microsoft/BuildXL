// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.Tracing;
using BuildXL.Storage.Fingerprints;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// StrongFingerprint subclass used to indicate a boundary between cache levels.
    /// <see cref="FollowingEntriesAreRemote"/> indicates whether entries enumerated *after* this sentinel
    /// are from the remote cache (true) or the local cache (false).
    /// </summary>
    [EventData]
    public sealed class StrongFingerprintSentinel : StrongFingerprint
    {
        /// <summary>
        /// When true, entries enumerated after this sentinel are remote (until the next sentinel or end of stream).
        /// When false, entries enumerated after this sentinel are local.
        /// </summary>
        public bool FollowingEntriesAreRemote { get; }

        /// <nodoc/>
        private StrongFingerprintSentinel(bool followingEntriesAreRemote)
            : base(WeakFingerprintHash.NoHash, CasHash.NoItem, new Hash(FingerprintUtilities.ZeroFingerprint), "Sentinel")
        {
            FollowingEntriesAreRemote = followingEntriesAreRemote;
        }

        /// <summary>
        /// The legacy sentinel instance. Entries after this sentinel are remote.
        /// Used by e.g. the VerticalAggregator which enumerates local first, then emits this, then remote.
        /// </summary>
        public static StrongFingerprintSentinel Instance { get; } = new StrongFingerprintSentinel(followingEntriesAreRemote: true);

        /// <summary>
        /// Sentinel indicating that entries after it are remote.
        /// </summary>
        public static StrongFingerprintSentinel RemoteFollows { get; } = Instance;

        /// <summary>
        /// Sentinel indicating that entries after it are local.
        /// </summary>
        public static StrongFingerprintSentinel LocalFollows { get; } = new StrongFingerprintSentinel(followingEntriesAreRemote: false);
    }
}
