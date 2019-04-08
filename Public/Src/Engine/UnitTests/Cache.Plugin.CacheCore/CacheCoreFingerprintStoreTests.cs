// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Engine.Cache.Plugin.CacheCore;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Engine.Cache.Plugin.CacheCore
{
    /// <summary>
    /// Tests for the <see cref="CacheCoreFingerprintStore"/> adapter.
    /// </summary>
    public sealed class CacheCoreFingerprintStoreTests : MemCacheTest
    {
        public readonly PipExecutionContext Context;
        public readonly CacheCoreArtifactContentCache ContentCache;
        public readonly CacheCoreFingerprintStore FingerprintStore;

        public CacheCoreFingerprintStoreTests(ITestOutputHelper output)
            : base(output)
        {
            Context = BuildXLContext.CreateInstanceForTesting();
            FingerprintStore = new CacheCoreFingerprintStore(Session);
            ContentCache = new CacheCoreArtifactContentCache(Session, rootTranslator: null);
        }

        [Fact]
        public async Task RoundtripCacheEntryWithMetadata()
        {
            ContentHash pathSetHash = await AddContent("This is a list of paths");
            WeakContentFingerprint weak = CreateWeakFingerprint("Fingerprint text here");
            StrongContentFingerprint strong = CreateStrongFingerprint("Strong fingerprint text here");

            CacheEntry entry = await CreateCacheEntryAndStoreContent("Metadata", "A", "B");
            Possible<CacheEntryPublishResult> maybePublished = await FingerprintStore.TryPublishCacheEntryAsync(
                weak,
                pathSetHash,
                strong,
                entry);
            XAssert.IsTrue(maybePublished.Succeeded);

            CacheEntryPublishResult publishResult = maybePublished.Result;
            XAssert.AreEqual(CacheEntryPublishStatus.Published, publishResult.Status);

            Possible<CacheEntry?> maybeFetched = await FingerprintStore.TryGetCacheEntryAsync(
                weak,
                pathSetHash,
                strong);
            XAssert.IsTrue(maybeFetched.Succeeded);
            XAssert.IsTrue(maybeFetched.Result.HasValue, "Unexpected miss (was just stored)");

            CacheEntry roundtrippedEntry = maybeFetched.Result.Value;
            AssertCacheEntriesEquivalent(entry, roundtrippedEntry);
        }

        [Fact]
        public async Task FailedPublishReturnsConflictingEntry()
        {
            ContentHash pathSetHash = await AddContent("This is a list of paths");
            WeakContentFingerprint weak = CreateWeakFingerprint("Fingerprint text here");
            StrongContentFingerprint strong = CreateStrongFingerprint("Strong fingerprint text here");

            CacheEntry originalEntry = await CreateCacheEntryAndStoreContent("Metadata", "A", "B");

            Possible<CacheEntryPublishResult> maybePublished = await FingerprintStore.TryPublishCacheEntryAsync(
                weak,
                pathSetHash,
                strong,
                originalEntry);
            XAssert.IsTrue(maybePublished.Succeeded);

            CacheEntryPublishResult publishResult = maybePublished.Result;
            XAssert.AreEqual(CacheEntryPublishStatus.Published, publishResult.Status);

            CacheEntry successorEntry = await CreateCacheEntryAndStoreContent("Metadata", "Different A", "Different B");

            Possible<CacheEntryPublishResult> maybePublishedAgain = await FingerprintStore.TryPublishCacheEntryAsync(
                weak,
                pathSetHash,
                strong,
                successorEntry);

            // The conflict info is in the result (not a failure).
            XAssert.IsTrue(maybePublishedAgain.Succeeded);
            CacheEntryPublishResult publishAgainResult = maybePublishedAgain.Result;
            XAssert.AreEqual(CacheEntryPublishStatus.RejectedDueToConflictingEntry, publishAgainResult.Status);

            // Original entry should be returned.
            AssertCacheEntriesEquivalent(publishAgainResult.ConflictingEntry, originalEntry);
        }

        [Fact]
        public async Task ListMultipleEntriesForSamePathSet()
        {
            ContentHash pathSetHash = await AddContent("This is a list of paths");
            WeakContentFingerprint weak = CreateWeakFingerprint("Fingerprint text here");
            StrongContentFingerprint strongA = CreateStrongFingerprint("Strong fingerprint text here");
            StrongContentFingerprint strongB = CreateStrongFingerprint("Slightly different fingerprint text here");

            CacheEntry entryA = await CreateCacheEntryAndStoreContent("Metadata A", "A", "B");
            CacheEntry entryB = await CreateCacheEntryAndStoreContent("Metadata B", "A-prime", "B-prime");

            await PublishExpectingNoConflict(weak, pathSetHash, strongA, entryA);
            await PublishExpectingNoConflict(weak, pathSetHash, strongB, entryB);

            List<PublishedEntryRef> refs = new List<PublishedEntryRef>();
            foreach (Task<Possible<PublishedEntryRef, Failure>> maybeEntryTask in FingerprintStore.ListPublishedEntriesByWeakFingerprint(weak))
            {
                Possible<PublishedEntryRef> maybeEntry = await maybeEntryTask;
                XAssert.IsTrue(maybeEntry.Succeeded);
                refs.Add(maybeEntry.Result);
            }

            if (refs[0].StrongFingerprint == strongA)
            {
                XAssert.AreEqual(strongB, refs[1].StrongFingerprint);
                XAssert.AreEqual(pathSetHash, refs[1].PathSetHash);
            }
            else
            {
                XAssert.AreEqual(strongB, refs[0].StrongFingerprint);
                XAssert.AreEqual(pathSetHash, refs[0].PathSetHash);

                XAssert.AreEqual(strongA, refs[1].StrongFingerprint);
                XAssert.AreEqual(pathSetHash, refs[1].PathSetHash);
            }
        }

        private async Task<CacheEntry> CreateCacheEntryAndStoreContent(string metadata, params string[] content)
        {
            ContentHash metadataHash = await AddContent(metadata);
            ContentHash[] hashes = new ContentHash[content.Length + 1];
            hashes[0] = metadataHash;
            for (int i = 0; i < content.Length; i++)
            {
                hashes[i + 1] = await AddContent(content[i]);
            }

            return CacheEntry.FromArray(ReadOnlyArray<ContentHash>.FromWithoutCopy(hashes), null);
        }

        private async Task PublishExpectingNoConflict(
            WeakContentFingerprint weak,
            ContentHash pathSetHash,
            StrongContentFingerprint strong,
            CacheEntry entry)
        {
            Possible<CacheEntryPublishResult> maybePublished = await FingerprintStore.TryPublishCacheEntryAsync(
                weak,
                pathSetHash,
                strong,
                entry);
            XAssert.IsTrue(maybePublished.Succeeded);

            CacheEntryPublishResult publishResult = maybePublished.Result;
            XAssert.AreEqual(CacheEntryPublishStatus.Published, publishResult.Status);
        }

        private static WeakContentFingerprint CreateWeakFingerprint(string content)
        {
            return new WeakContentFingerprint(FingerprintUtilities.Hash(content));
        }

        private static StrongContentFingerprint CreateStrongFingerprint(string content)
        {
            return new StrongContentFingerprint(FingerprintUtilities.Hash(content));
        }

        private static void AssertCacheEntriesEquivalent(CacheEntry a, CacheEntry b)
        {
            XAssert.AreEqual(a.MetadataHash, b.MetadataHash);
            XAssert.AreEqual(a.ReferencedContent.Length, b.ReferencedContent.Length);
            for (int i = 0; i < a.ReferencedContent.Length; i++)
            {
                XAssert.AreEqual(a.ReferencedContent[i], b.ReferencedContent[i]);
            }
        }
    }
}
