// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Vsts
{
    /// <summary>
    ///     Used to keep track fingerprints that need to be incorporated.
    /// </summary>
    /// <remarks>
    ///     This data structure needs to be thread safe and support de-duplication
    /// </remarks>
    internal class FingerprintTracker
    {
        /// <summary>
        ///     How far into the expiry range to set the new minimum (for the purpose of not publishing new almost-stale values).
        /// </summary>
        public const double RangeFactor = .5;

        // Mapping of fingerprint to freshness. Fresh fingerprints do not need their expiration refreshed.
        private readonly ConcurrentDictionary<StrongFingerprint, bool> _fingerprintFreshnessMap;

        // Minimum time-to-live for a value to be considered "fresh enough" and thus not in need of refreshing the value's expiratio
        private readonly DateTime _expiryMinimum;

        // No use publishing new values that are imminently stale, so we've chosen a further minimum expiry for new values.
        private readonly DateTime _newExpiryMinimum;

        // The range of milliseconds beyond the new minimum to extend fingerprint expiry
        private readonly double _newExpiryRangeMilliseconds;

        /// <summary>
        ///     Gets all tracked fingerprints that are not sufficiently fresh
        /// </summary>
        internal IEnumerable<StrongFingerprint> StaleFingerprints => _fingerprintFreshnessMap.Where(mapping => !mapping.Value).Select(mapping => mapping.Key);

        /// <summary>
        ///     Gets the fingerprint count
        /// </summary>
        internal int Count => _fingerprintFreshnessMap.Count;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FingerprintTracker"/> class.
        /// </summary>
        internal FingerprintTracker(DateTime expiryMinimum, TimeSpan expiryRange)
        {
            _fingerprintFreshnessMap = new ConcurrentDictionary<StrongFingerprint, bool>();
            _expiryMinimum = expiryMinimum;
            _newExpiryMinimum = _expiryMinimum + TimeSpan.FromMilliseconds(expiryRange.TotalMilliseconds * RangeFactor);
            _newExpiryRangeMilliseconds = expiryRange.TotalMilliseconds * (1 - RangeFactor);
        }

        /// <summary>
        ///     Adds a fingerprint to the track list
        /// </summary>
        /// <param name="strongFingerprint">Fingerprint to track</param>
        /// <param name="knownExpiration">The known expiration for the fingerprint's value</param>
        /// <remarks>
        ///     The expiration is used to determine whether the fingerprint's value's expiration/TTL needs to be refreshed/updated in the service.
        /// </remarks>
        internal void Track(StrongFingerprint strongFingerprint, DateTime? knownExpiration = null)
        {
            if (knownExpiration.HasValue && knownExpiration.Value >= _expiryMinimum)
            {
                // If any source says that the value is fresh enough, then it is.
                _fingerprintFreshnessMap.AddOrUpdate(strongFingerprint, true, (sfp, oldFreshness) => true);
            }
            else
            {
                // Don't overwrite a record of freshness just because another source doesn't know that it's fresh.
                _fingerprintFreshnessMap.TryAdd(strongFingerprint, false);
            }
        }

        /// <summary>
        ///     Generates a new expiration within the configured ranges.
        /// </summary>
        /// <remarks>
        ///     Returns a value with a minimum of halfway through the range so that values aren't considered immediately stale.
        ///     Also, returns a random value within the range so that values don't all become stale at once. This servs to spread the
        ///     server calls across multiple sessions.
        /// </remarks>
        internal DateTime GenerateNewExpiration()
        {
            return _newExpiryMinimum + TimeSpan.FromMilliseconds(_newExpiryRangeMilliseconds * ThreadSafeRandom.Generator.NextDouble());
        }
    }
}
