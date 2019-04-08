// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using ContentStoreTest.Test;
using FluentAssertions;
using BuildXL.Cache.MemoizationStore.Vsts;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.VstsTest
{
    public class FingerprintTrackerTests : TestBase
    {
        public FingerprintTrackerTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public void GeneratesExpirationInSecondHalfOfRange()
        {
            DateTime expiryMinimum = DateTime.UtcNow + TimeSpan.FromHours(1);
            TimeSpan expiryRange = TimeSpan.FromHours(10);
            FingerprintTracker tracker = new FingerprintTracker(expiryMinimum, expiryRange);

            for (int i = 0; i < 1000; i++)
            {
                DateTime newExpiration = tracker.GenerateNewExpiration();
                Assert.InRange(
                    newExpiration,
                    expiryMinimum + TimeSpan.FromMilliseconds(expiryRange.TotalMilliseconds * FingerprintTracker.RangeFactor),
                    expiryMinimum + expiryRange);
            }
        }

        [Fact]
        public void TracksUnknownFingerprintAsStale()
        {
            DateTime expiryMinimum = DateTime.UtcNow + TimeSpan.FromHours(1);
            TimeSpan expiryRange = TimeSpan.FromHours(10);
            FingerprintTracker tracker = new FingerprintTracker(expiryMinimum, expiryRange);

            StrongFingerprint fingerprint = StrongFingerprint.Random();
            tracker.Track(fingerprint, knownExpiration: null);

            var staleFingerprints = tracker.StaleFingerprints.ToList();
            staleFingerprints.Count.Should().Be(1);
            staleFingerprints[0].Should().Be(fingerprint);
        }

        [Fact]
        public void TracksStaleFingerprintAsStale()
        {
            DateTime expiryMinimum = DateTime.UtcNow + TimeSpan.FromHours(1);
            TimeSpan expiryRange = TimeSpan.FromHours(10);
            FingerprintTracker tracker = new FingerprintTracker(expiryMinimum, expiryRange);

            StrongFingerprint fingerprint = StrongFingerprint.Random();
            tracker.Track(fingerprint, expiryMinimum - TimeSpan.FromMinutes(10));
            tracker.Count.Should().Be(1);

            var staleFingerprints = tracker.StaleFingerprints.ToList();
            staleFingerprints.Count.Should().Be(1);
            staleFingerprints[0].Should().Be(fingerprint);
        }

        [Fact]
        public void TracksFreshFingerprintAsFresh()
        {
            DateTime expiryMinimum = DateTime.UtcNow + TimeSpan.FromHours(1);
            TimeSpan expiryRange = TimeSpan.FromHours(10);
            FingerprintTracker tracker = new FingerprintTracker(expiryMinimum, expiryRange);

            StrongFingerprint fingerprint = StrongFingerprint.Random();
            tracker.Track(fingerprint, expiryMinimum + TimeSpan.FromMinutes(10));
            tracker.Count.Should().Be(1);

            var staleFingerprints = tracker.StaleFingerprints.ToList();
            staleFingerprints.Count.Should().Be(0);
        }

        [Fact]
        public void FreshOverwritesStale()
        {
            DateTime expiryMinimum = DateTime.UtcNow + TimeSpan.FromHours(1);
            TimeSpan expiryRange = TimeSpan.FromHours(10);
            FingerprintTracker tracker = new FingerprintTracker(expiryMinimum, expiryRange);

            StrongFingerprint fingerprint = StrongFingerprint.Random();
            tracker.Track(fingerprint, expiryMinimum - TimeSpan.FromMinutes(10));
            tracker.Track(fingerprint, expiryMinimum + TimeSpan.FromMinutes(10));
            tracker.Count.Should().Be(1);

            var staleFingerprints = tracker.StaleFingerprints.ToList();
            staleFingerprints.Count.Should().Be(0);
        }

        [Fact]
        public void StaleDoesNotOverwriteFresh()
        {
            DateTime expiryMinimum = DateTime.UtcNow + TimeSpan.FromHours(1);
            TimeSpan expiryRange = TimeSpan.FromHours(10);
            FingerprintTracker tracker = new FingerprintTracker(expiryMinimum, expiryRange);

            StrongFingerprint fingerprint = StrongFingerprint.Random();
            tracker.Track(fingerprint, expiryMinimum + TimeSpan.FromMinutes(10));
            tracker.Track(fingerprint, expiryMinimum - TimeSpan.FromMinutes(10));
            tracker.Count.Should().Be(1);

            var staleFingerprints = tracker.StaleFingerprints.ToList();
            staleFingerprints.Count.Should().Be(0);
        }
    }
}
