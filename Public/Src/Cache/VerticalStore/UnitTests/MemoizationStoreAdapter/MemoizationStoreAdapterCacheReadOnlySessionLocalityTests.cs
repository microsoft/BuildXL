// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStoreAdapter;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Core;
using ContentStoreTest.Test;
using Xunit;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using BuildXLStrongFingerprint = BuildXL.Cache.Interfaces.StrongFingerprint;
using MemoizationICacheSession = BuildXL.Cache.MemoizationStore.Interfaces.Sessions.ICacheSession;
using MemoizationStrongFingerprint = BuildXL.Cache.MemoizationStore.Interfaces.Sessions.StrongFingerprint;

namespace Test.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// Focused tests for the locality-translation logic in
    /// <see cref="MemoizationStoreAdapterCacheReadOnlySession.EnumerateStrongFingerprints"/>.
    /// These tests feed a hand-built stream of <see cref="GetSelectorResult"/> values (some tagged with
    /// <see cref="SelectorSourceCacheLevel"/>) through a stub <see cref="MemoizationICacheSession"/> and
    /// assert that the adapter inserts <see cref="StrongFingerprintSentinel"/> markers in the right
    /// positions and with the right "follows" semantics.
    /// </summary>
    public sealed class MemoizationStoreAdapterCacheReadOnlySessionLocalityTests
    {
        [Fact]
        public async Task UntaggedSelectorsProduceNoSentinels()
        {
            List<Possible<BuildXLStrongFingerprint, Failure>> results = await TranslateAsync(
                MakeResult(level: null),
                MakeResult(level: null));

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.IsNotType<StrongFingerprintSentinel>(r.Result));
        }

        [Fact]
        public async Task TaggedLocalThenTaggedRemoteEmitsSingleRemoteFollowsSentinel()
        {
            // Producer convention: tag only the *first* selector of each contiguous run from a given level.
            List<Possible<BuildXLStrongFingerprint, Failure>> results = await TranslateAsync(
                MakeResult(SelectorSourceCacheLevel.Local),
                MakeResult(level: null),
                MakeResult(SelectorSourceCacheLevel.Remote),
                MakeResult(level: null));

            Assert.Equal(5, results.Count);

            // The 3rd entry is the inserted sentinel marking the local->remote transition.
            AssertSentinel(results[0], expectIsSentinel: false);
            AssertSentinel(results[1], expectIsSentinel: false);
            AssertSentinel(results[2], expectIsSentinel: true, expectFollowingIsRemote: true);
            AssertSentinel(results[3], expectIsSentinel: false);
            AssertSentinel(results[4], expectIsSentinel: false);
        }

        [Fact]
        public async Task TaggedRemoteFirstEmitsLeadingRemoteFollowsSentinel()
        {
            // The starting locality assumed by the consumer is Local. If the first selector is Remote,
            // the adapter must emit a leading sentinel so that the consumer correctly attributes the
            // entries that follow it.
            List<Possible<BuildXLStrongFingerprint, Failure>> results = await TranslateAsync(
                MakeResult(SelectorSourceCacheLevel.Remote),
                MakeResult(level: null),
                MakeResult(SelectorSourceCacheLevel.Local),
                MakeResult(level: null));

            Assert.Equal(6, results.Count);

            AssertSentinel(results[0], expectIsSentinel: true, expectFollowingIsRemote: true);
            AssertSentinel(results[1], expectIsSentinel: false);
            AssertSentinel(results[2], expectIsSentinel: false);
            AssertSentinel(results[3], expectIsSentinel: true, expectFollowingIsRemote: false);
            AssertSentinel(results[4], expectIsSentinel: false);
            AssertSentinel(results[5], expectIsSentinel: false);
        }

        [Fact]
        public async Task AllLocalEmitsNoSentinels()
        {
            // Default starting locality is Local, so a stream that only ever tags Local should not
            // produce any sentinel transitions.
            List<Possible<BuildXLStrongFingerprint, Failure>> results = await TranslateAsync(
                MakeResult(SelectorSourceCacheLevel.Local),
                MakeResult(level: null),
                MakeResult(level: null));

            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.IsNotType<StrongFingerprintSentinel>(r.Result));
        }

        [Fact]
        public async Task EmptyStreamYieldsSingleSentinel()
        {
            // EnumerateStrongFingerprints must yield at least one element; when the underlying source
            // produces nothing, the adapter falls back to a single sentinel.
            List<Possible<BuildXLStrongFingerprint, Failure>> results = await TranslateAsync();

            var single = Assert.Single(results);
            AssertSentinel(single, expectIsSentinel: true);
        }

        private static GetSelectorResult MakeResult(SelectorSourceCacheLevel? level)
        {
            // Selector.Output is converted via FingerprintUtilities.CreateFrom, which requires exactly 20 bytes.
            Selector selector = Selector.Random(outputLengthBytes: 20);
            return level.HasValue
                ? new GetSelectorResult(selector, level.Value)
                : new GetSelectorResult(selector);
        }

        private static void AssertSentinel(
            Possible<BuildXLStrongFingerprint, Failure> result,
            bool expectIsSentinel,
            bool? expectFollowingIsRemote = null)
        {
            Assert.True(result.Succeeded, result.Succeeded ? null : result.Failure.Describe());
            if (expectIsSentinel)
            {
                var sentinel = Assert.IsType<StrongFingerprintSentinel>(result.Result);
                if (expectFollowingIsRemote.HasValue)
                {
                    Assert.Equal(expectFollowingIsRemote.Value, sentinel.FollowingEntriesAreRemote);
                }
            }
            else
            {
                Assert.IsNotType<StrongFingerprintSentinel>(result.Result);
            }
        }

        private static async Task<List<Possible<BuildXLStrongFingerprint, Failure>>> TranslateAsync(params GetSelectorResult[] selectorResults)
        {
            var stub = new StubMemoizationCacheSession(selectorResults);

            // The adapter session uses the inner cache only for GetStatisticsAsync (not invoked by this test);
            // passing null is safe for EnumerateStrongFingerprints.
            var adapter = new MemoizationStoreAdapterCacheReadOnlySession(
                readOnlyCacheSession: stub,
                cache: null,
                cacheId: new CacheId("TestAdapter"),
                logger: TestGlobal.Logger);

            var collected = new List<Possible<BuildXLStrongFingerprint, Failure>>();
            foreach (Task<Possible<BuildXLStrongFingerprint, Failure>> promise in adapter.EnumerateStrongFingerprints(WeakFingerprintHash.Random(), default(OperationHints), Guid.NewGuid()))
            {
                collected.Add(await promise);
            }

            return collected;
        }

        /// <summary>
        /// Minimal <see cref="MemoizationICacheSession"/> stub that returns a hand-built sequence of
        /// <see cref="GetSelectorResult"/> values from <see cref="GetSelectors"/>. All other operations
        /// throw <see cref="NotImplementedException"/>.
        /// </summary>
        private sealed class StubMemoizationCacheSession : MemoizationICacheSession
        {
            private readonly GetSelectorResult[] m_selectorResults;

            public StubMemoizationCacheSession(GetSelectorResult[] selectorResults)
            {
                m_selectorResults = selectorResults;
            }

            public string Name => nameof(StubMemoizationCacheSession);

            public bool StartupCompleted => true;

            public bool StartupStarted => true;

            public bool ShutdownCompleted => false;

            public bool ShutdownStarted => false;

            public void Dispose()
            {
            }

            public Task<BoolResult> StartupAsync(Context context) => Task.FromResult(BoolResult.Success);

            public Task<BoolResult> ShutdownAsync(Context context) => Task.FromResult(BoolResult.Success);

            public async IAsyncEnumerable<GetSelectorResult> GetSelectors(
                Context context,
                Fingerprint weakFingerprint,
                [EnumeratorCancellation] CancellationToken cts,
                UrgencyHint urgencyHint = UrgencyHint.Nominal)
            {
                foreach (GetSelectorResult result in m_selectorResults)
                {
                    yield return result;
                }

                await Task.CompletedTask;
            }

            public Task<GetContentHashListResult> GetContentHashListAsync(
                Context context, MemoizationStrongFingerprint strongFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<PinResult> PinAsync(
                Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<OpenStreamResult> OpenStreamAsync(
                Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<PlaceFileResult> PlaceFileAsync(
                Context context,
                ContentHash contentHash,
                AbsolutePath path,
                FileAccessMode accessMode,
                FileReplacementMode replacementMode,
                FileRealizationMode realizationMode,
                CancellationToken cts,
                UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
                Context context,
                IReadOnlyList<ContentHash> contentHashes,
                CancellationToken cts,
                UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
                Context context,
                IReadOnlyList<ContentHash> contentHashes,
                PinOperationConfiguration config) => throw new NotImplementedException();

            public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(
                Context context,
                IReadOnlyList<ContentHashWithPath> hashesWithPaths,
                FileAccessMode accessMode,
                FileReplacementMode replacementMode,
                FileRealizationMode realizationMode,
                CancellationToken cts,
                UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(
                Context context,
                MemoizationStrongFingerprint strongFingerprint,
                ContentHashListWithDeterminism contentHashListWithDeterminism,
                CancellationToken cts,
                UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<BoolResult> IncorporateStrongFingerprintsAsync(
                Context context,
                IEnumerable<Task<MemoizationStrongFingerprint>> strongFingerprints,
                CancellationToken cts,
                UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<PutResult> PutFileAsync(
                Context context,
                HashType hashType,
                AbsolutePath path,
                FileRealizationMode realizationMode,
                CancellationToken cts,
                UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<PutResult> PutFileAsync(
                Context context,
                ContentHash contentHash,
                AbsolutePath path,
                FileRealizationMode realizationMode,
                CancellationToken cts,
                UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<PutResult> PutStreamAsync(
                Context context, HashType hashType, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<PutResult> PutStreamAsync(
                Context context, ContentHash contentHash, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();
        }
    }
}
