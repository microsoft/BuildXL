// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.Interfaces;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Engine.Cache.Plugin.CacheCore;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Core;
using Xunit;

namespace Test.BuildXL.Engine.Cache.Plugin.CacheCore
{
    /// <summary>
    /// Focused tests for the locality-attribution logic in
    /// <see cref="CacheCoreFingerprintStore.ListPublishedEntriesByWeakFingerprint"/>. These tests feed a
    /// hand-built strong-fingerprint stream (with <see cref="StrongFingerprintSentinel"/> markers) through a
    /// stub <see cref="ICacheSession"/> and assert that the resulting <see cref="PublishedEntryRefLocality"/>
    /// values match the sentinel-defined boundaries.
    /// </summary>
    public sealed class CacheCoreFingerprintStoreLocalityTests
    {
        [Fact]
        public async Task DefaultsToLocalWhenNoSentinelPresent()
        {
            StrongFingerprint a = CreateStrongFingerprint("Cache1");
            StrongFingerprint b = CreateStrongFingerprint("Cache1");

            List<PublishedEntryRef> refs = await CollectAsync(a, b);

            Assert.Equal(2, refs.Count);
            Assert.All(refs, r => Assert.Equal(PublishedEntryRefLocality.Local, r.Locality));
        }

        [Fact]
        public async Task LocalThenRemoteAttributesLocalityCorrectly()
        {
            StrongFingerprint local1 = CreateStrongFingerprint("Local");
            StrongFingerprint local2 = CreateStrongFingerprint("Local");
            StrongFingerprint remote1 = CreateStrongFingerprint("Remote");

            List<PublishedEntryRef> refs = await CollectAsync(
                local1,
                local2,
                StrongFingerprintSentinel.RemoteFollows,
                remote1);

            Assert.Equal(3, refs.Count);
            Assert.Equal(PublishedEntryRefLocality.Local, refs[0].Locality);
            Assert.Equal(PublishedEntryRefLocality.Local, refs[1].Locality);
            Assert.Equal(PublishedEntryRefLocality.Remote, refs[2].Locality);
        }

        [Fact]
        public async Task RemoteFirstThenLocalAttributesLocalityCorrectly()
        {
            StrongFingerprint remote1 = CreateStrongFingerprint("Remote");
            StrongFingerprint remote2 = CreateStrongFingerprint("Remote");
            StrongFingerprint local1 = CreateStrongFingerprint("Local");

            // A leading RemoteFollows sentinel re-classifies the entries that follow it as Remote
            // (otherwise they would default to Local). A trailing LocalFollows sentinel restores Local.
            List<PublishedEntryRef> refs = await CollectAsync(
                StrongFingerprintSentinel.RemoteFollows,
                remote1,
                remote2,
                StrongFingerprintSentinel.LocalFollows,
                local1);

            Assert.Equal(3, refs.Count);
            Assert.Equal(PublishedEntryRefLocality.Remote, refs[0].Locality);
            Assert.Equal(PublishedEntryRefLocality.Remote, refs[1].Locality);
            Assert.Equal(PublishedEntryRefLocality.Local, refs[2].Locality);
        }

        [Fact]
        public async Task LegacySentinelInstanceIsRemoteFollows()
        {
            // The legacy StrongFingerprintSentinel.Instance keeps its historical semantics
            // (a single boundary between local-first and remote-second cache layers).
            StrongFingerprint local1 = CreateStrongFingerprint("Local");
            StrongFingerprint remote1 = CreateStrongFingerprint("Remote");

            List<PublishedEntryRef> refs = await CollectAsync(
                local1,
                StrongFingerprintSentinel.Instance,
                remote1);

            Assert.Equal(2, refs.Count);
            Assert.Equal(PublishedEntryRefLocality.Local, refs[0].Locality);
            Assert.Equal(PublishedEntryRefLocality.Remote, refs[1].Locality);
        }

        [Fact]
        public async Task SentinelOnlyStreamReturnsNoVisibleEntries()
        {
            List<PublishedEntryRef> refs = await CollectAsync(
                StrongFingerprintSentinel.RemoteFollows,
                StrongFingerprintSentinel.LocalFollows);

            Assert.Empty(refs);
        }

        [Fact]
        public async Task EmptyStreamReturnsNoEntries()
        {
            List<PublishedEntryRef> refs = await CollectAsync();
            Assert.Empty(refs);
        }

        [Fact]
        public async Task SentinelArrivingAsNotYetCompletedFirstTaskStillUpdatesLocality()
        {
            // The MemoizationStoreAdapter contract is that the first yielded task may not be completed
            // when the consumer's MoveNext() returns; subsequent tasks are Task.FromResult instances.
            // This test simulates that pattern: the first emitted entry is a delayed (not yet completed)
            // Task that resolves to a RemoteFollows sentinel. The store must still observe the sentinel
            // and tag the following entry as Remote (rather than the default Local).
            StrongFingerprint remote1 = CreateStrongFingerprint("Remote");

            var firstTcs = new TaskCompletionSource<Possible<StrongFingerprint, Failure>>();
            var promises = new List<Task<Possible<StrongFingerprint, Failure>>>
            {
                firstTcs.Task,
                Task.FromResult(new Possible<StrongFingerprint, Failure>(remote1)),
            };

            // Complete the first task only after a short delay so it is genuinely not completed when
            // ListPublishedEntriesByWeakFingerprint inspects it.
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                firstTcs.SetResult(new Possible<StrongFingerprint, Failure>(StrongFingerprintSentinel.RemoteFollows));
            });

            var stub = new StubCacheSession(promises);
            var store = new CacheCoreFingerprintStore(stub);
            var weak = new WeakContentFingerprint(FingerprintUtilities.Hash("any-weak"));

            var collected = new List<PublishedEntryRef>();
            foreach (Task<Possible<PublishedEntryRef, Failure>> promise in store.ListPublishedEntriesByWeakFingerprint(weak))
            {
                Possible<PublishedEntryRef, Failure> maybe = await promise;
                Assert.True(maybe.Succeeded, maybe.Succeeded ? null : maybe.Failure.Describe());
                if (!maybe.Result.IgnoreEntry)
                {
                    collected.Add(maybe.Result);
                }
            }

            Assert.Single(collected);
            Assert.Equal(PublishedEntryRefLocality.Remote, collected[0].Locality);
        }

        private static async Task<List<PublishedEntryRef>> CollectAsync(params StrongFingerprint[] entries)
        {
            var stub = new StubCacheSession(entries.Select(e => Task.FromResult(new Possible<StrongFingerprint, Failure>(e))));
            var store = new CacheCoreFingerprintStore(stub);
            var weak = new WeakContentFingerprint(FingerprintUtilities.Hash("any-weak"));

            var collected = new List<PublishedEntryRef>();
            foreach (Task<Possible<PublishedEntryRef, Failure>> promise in store.ListPublishedEntriesByWeakFingerprint(weak))
            {
                Possible<PublishedEntryRef, Failure> maybe = await promise;
                Assert.True(maybe.Succeeded, maybe.Succeeded ? null : maybe.Failure.Describe());
                if (!maybe.Result.IgnoreEntry)
                {
                    collected.Add(maybe.Result);
                }
            }

            return collected;
        }

        private static StrongFingerprint CreateStrongFingerprint(string cacheId)
        {
            return new StrongFingerprint(
                WeakFingerprintHash.Random(),
                new CasHash(new Hash(ContentHashingUtilities.CreateRandom())),
                new Hash(FingerprintUtilities.CreateRandom()),
                cacheId);
        }

        /// <summary>
        /// Minimal <see cref="ICacheSession"/> stub whose only purpose is to return a hand-built
        /// strong-fingerprint stream from <see cref="EnumerateStrongFingerprints"/>. All other
        /// operations throw <see cref="NotImplementedException"/>.
        /// </summary>
        private sealed class StubCacheSession : ICacheSession
        {
            private readonly List<Task<Possible<StrongFingerprint, Failure>>> m_entries;

            public StubCacheSession(IEnumerable<Task<Possible<StrongFingerprint, Failure>>> entries)
            {
                m_entries = entries.ToList();
            }

            public CacheId CacheId { get; } = new CacheId("StubCacheSession");

            public string CacheSessionId => null;

            public bool IsClosed => false;

            public bool StrictMetadataCasCoupling => false;

            public Task<Possible<string, Failure>> CloseAsync(Guid activityId = default) => throw new NotImplementedException();

            public IEnumerable<Task<Possible<StrongFingerprint, Failure>>> EnumerateStrongFingerprints(WeakFingerprintHash weak, OperationHints hints = default, Guid activityId = default)
            {
                foreach (Task<Possible<StrongFingerprint, Failure>> entry in m_entries)
                {
                    yield return entry;
                }
            }

            public Task<Possible<CasEntries, Failure>> GetCacheEntryAsync(StrongFingerprint strong, OperationHints hints = default, Guid activityId = default) => throw new NotImplementedException();

            public Task<Possible<string, Failure>> PinToCasAsync(CasHash hash, CancellationToken cancellationToken, OperationHints hints = default, Guid activityId = default) => throw new NotImplementedException();

            public Task<Possible<string, Failure>[]> PinToCasAsync(CasEntries hashes, CancellationToken cancellationToken, OperationHints hints = default, Guid activityId = default) => throw new NotImplementedException();

            public Task<Possible<string, Failure>> ProduceFileAsync(CasHash hash, string filename, FileState fileState, OperationHints hints = default, Guid activityId = default, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Possible<StreamWithLength, Failure>> GetStreamAsync(CasHash hash, OperationHints hints = default, Guid activityId = default) => throw new NotImplementedException();

            public Task<Possible<CacheSessionStatistics[], Failure>> GetStatisticsAsync(Guid activityId = default) => throw new NotImplementedException();

            public Task<Possible<ValidateContentStatus, Failure>> ValidateContentAsync(CasHash hash, UrgencyHint urgencyHint = UrgencyHint.Nominal, Guid activityId = default) => throw new NotImplementedException();

            public Task<Possible<CasHash, Failure>> AddToCasAsync(string filename, FileState fileState, CasHash? hash = null, UrgencyHint urgencyHint = UrgencyHint.Nominal, Guid activityId = default) => throw new NotImplementedException();

            public Task<Possible<CasHash, Failure>> AddToCasAsync(Stream filestream, CasHash? hash = null, UrgencyHint urgencyHint = UrgencyHint.Nominal, Guid activityId = default) => throw new NotImplementedException();

            public Task<Possible<FullCacheRecordWithDeterminism, Failure>> AddOrGetAsync(WeakFingerprintHash weak, CasHash casElement, Hash hashElement, CasEntries hashes, UrgencyHint urgencyHint = UrgencyHint.Nominal, Guid activityId = default) => throw new NotImplementedException();

            public Task<Possible<int, Failure>> IncorporateRecordsAsync(IEnumerable<Task<StrongFingerprint>> strongFingerprints, Guid activityId = default) => throw new NotImplementedException();

            public IEnumerable<Task<StrongFingerprint>> EnumerateSessionFingerprints(Guid activityId = default) => throw new NotImplementedException();

            public Task<Possible<ContentDeleteStatus, Failure>> DeleteContentAsync(CasHash hash, CancellationToken cancellationToken = default, Guid activityId = default) => throw new NotImplementedException();
        }
    }
}
