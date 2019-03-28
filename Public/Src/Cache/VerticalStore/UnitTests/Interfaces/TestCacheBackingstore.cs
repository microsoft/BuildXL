// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.Interfaces.Test;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Cache.Tests
{
    /// <summary>
    /// This is an abstract class that provides core cache testing for
    /// direct backing stores.  Aggregators should not subclass this
    /// test set unless they specifically have backing store behaviors.
    ///
    /// Subclassing this class will automatically get you the core
    /// cache tests such that backing store tests are a superset of
    /// the core tests.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public abstract class TestCacheBackingstore : TestCacheBasicTests
    {
        private const int Determinism_None = 0;
        private const int Determinism_Cache1 = 1;
        private const int Determinism_Cache2 = 2;
        private const int Determinism_Tool = 3;

        private static readonly CacheDeterminism[] s_determinism =
        {
            CacheDeterminism.None,
            CacheDeterminism.ViaCache(new Guid("2ABD62E7-6117-4D8B-ABB2-D3346E5AF102"), CacheDeterminism.NeverExpires),
            CacheDeterminism.ViaCache(new Guid("78559E55-E0C3-4C77-A908-8AE9E6590764"), CacheDeterminism.NeverExpires),
            CacheDeterminism.Tool
        };

        /// <summary>
        /// These determinism conversions should happen
        /// </summary>
        /// <param name="fromDeterminism">First build determinism</param>
        /// <param name="toDeterminism">Second build determinism</param>
        /// <param name="differentCasEntries">True if the second build should have different CAS entries</param>
        /// <returns>void - fails test if broken</returns>
        [Theory]
        [InlineData(Determinism_None, Determinism_Tool, false)]
        [InlineData(Determinism_None, Determinism_Cache1, false)]
        [InlineData(Determinism_Cache1, Determinism_Cache2, false)]
        [InlineData(Determinism_Cache2, Determinism_Cache1, false)]
        [InlineData(Determinism_Cache1, Determinism_Tool, false)]
        [InlineData(Determinism_None, Determinism_Tool, true)]
        [InlineData(Determinism_None, Determinism_Cache1, true)]
        [InlineData(Determinism_Cache1, Determinism_Cache2, true)]
        [InlineData(Determinism_Cache2, Determinism_Cache1, true)]
        [InlineData(Determinism_Cache1, Determinism_Tool, true)]
        public async Task DeterminismUpgraded(int fromDeterminism, int toDeterminism, bool differentCasEntries)
        {
                string testName = I($"DeterminismUpgraded{fromDeterminism}x{toDeterminism}{(differentCasEntries ? "Diff" : "Same")}");
                string testCacheId = MakeCacheId(testName);
                ICache cache = await CreateCacheAsync(testCacheId);

                string testSessionId = "Session1-" + testCacheId;
                ICacheSession session = await CreateSessionAsync(cache, testSessionId);

                // We need at least 2 to make "differentCasEntries" work
                PipDefinition[] pips =
                {
                new PipDefinition("PipA", determinism: s_determinism[fromDeterminism]),
                new PipDefinition("PipB", determinism: s_determinism[fromDeterminism])
                };

                var records = (await pips.BuildAsync(session)).ToArray();

                await CloseSessionAsync(session, testSessionId);

                testSessionId = "Session2-" + testCacheId;
                session = await CreateSessionAsync(cache, testSessionId);

                // What we will do here is AddOrGet() a record with the determinism bit changed.
                for (int i = 0; i < records.Length; i++)
                {
                    var record = records[i];

                    // This gets the CasEntries we want
                    CasEntries newEntries = records[(i + (differentCasEntries ? 1 : 0)) % records.Length].CasEntries;

                    // Validate that the entry for the record is what we expect
                    var entries = (await session.GetCacheEntryAsync(record.StrongFingerprint)).Success();
                    XAssert.AreEqual(s_determinism[fromDeterminism].EffectiveGuid, entries.Determinism.EffectiveGuid);

                    // Now pin the CasElement and all of the CasEntries
                    (await session.PinToCasAsync(record.StrongFingerprint.CasElement)).Success();
                    (await session.PinToCasAsync(newEntries)).Success();

                    // Now make a new record
                    var newRecord = (await session.AddOrGetAsync(
                        record.StrongFingerprint.WeakFingerprint,
                                                                 record.StrongFingerprint.CasElement,
                                                                 record.StrongFingerprint.HashElement,
                                                                 new CasEntries(newEntries, s_determinism[toDeterminism]))).Success();

                    // The new record should be null since the determinism was upgraded
                    XAssert.IsNull(newRecord.Record);

                    // Now, we will try to get the same record from the cache to validate
                    // the setting of the bit
                    entries = (await session.GetCacheEntryAsync(record.StrongFingerprint)).Success();
                    XAssert.AreEqual(newEntries, entries);
                    XAssert.AreEqual(s_determinism[toDeterminism].EffectiveGuid, entries.Determinism.EffectiveGuid);
                }

                await CloseSessionAsync(session, testSessionId);
                await ShutdownCacheAsync(cache, testCacheId);           
        }

        /// <summary>
        /// These determinism conversions should *not* happen
        /// </summary>
        /// <param name="fromDeterminism">First build determinism</param>
        /// <param name="toDeterminism">Second build determinism</param>
        /// <param name="differentCasEntries">True if the second build should have different CAS entries</param>
        /// <returns>void - fails test if broken</returns>
        [Theory]
        [InlineData(Determinism_Tool, Determinism_None, false)]
        [InlineData(Determinism_Tool, Determinism_Cache1, false)]
        [InlineData(Determinism_Cache1, Determinism_None, false)]
        [InlineData(Determinism_Tool, Determinism_None, true)]
        [InlineData(Determinism_Tool, Determinism_Cache1, true)]
        [InlineData(Determinism_Cache1, Determinism_None, true)]
        public async Task DeterminismNotUpgraded(int fromDeterminism, int toDeterminism, bool differentCasEntries)
        {
            string testName = I($"DeterminismNotUpgraded{fromDeterminism}x{toDeterminism}{(differentCasEntries ? "Diff" : "Same")}");
            string testCacheId = MakeCacheId(testName);
            ICache cache = await CreateCacheAsync(testCacheId);

            string testSessionId = "Session1-" + testCacheId;
            ICacheSession session = await CreateSessionAsync(cache, testSessionId);

            // We need at least 2 to make "differentCasEntries" work
            PipDefinition[] pips =
            {
                new PipDefinition("PipA", determinism: s_determinism[fromDeterminism]),
                new PipDefinition("PipB", determinism: s_determinism[fromDeterminism])
            };

            var records = (await pips.BuildAsync(session)).ToArray();

            await CloseSessionAsync(session, testSessionId);

            testSessionId = "Session2-" + testCacheId;
            session = await CreateSessionAsync(cache, testSessionId);

            // What we will do here is AddOrGet() a record with the determinism bit changed.
            for (int i = 0; i < records.Length; i++)
            {
                var record = records[i];

                // This gets the CasEntries we want
                CasEntries newEntries = records[(i + (differentCasEntries ? 1 : 0)) % records.Length].CasEntries;

                // Validate that the entry for the record is what we expect
                var entries = (await session.GetCacheEntryAsync(record.StrongFingerprint)).Success();
                XAssert.AreEqual(s_determinism[fromDeterminism].EffectiveGuid, entries.Determinism.EffectiveGuid);

                // Now pin the CasElement and all of the CasEntries
                (await session.PinToCasAsync(record.StrongFingerprint.CasElement)).Success();
                (await session.PinToCasAsync(newEntries)).Success();

                // Now make a new record
                var newRecord = (await session.AddOrGetAsync(
                    record.StrongFingerprint.WeakFingerprint,
                                                             record.StrongFingerprint.CasElement,
                                                             record.StrongFingerprint.HashElement,
                                                             new CasEntries(newEntries, s_determinism[toDeterminism]))).Success();

                // The new record should be null since the contents were the same.
                if (differentCasEntries)
                {
                    XAssert.IsNotNull(newRecord.Record);
                    XAssert.AreEqual(record, newRecord.Record);
                }
                else
                {
                    XAssert.IsNull(newRecord.Record);
                }

                // Now, we will try to get the same record from the cache to validate
                // the setting of the bit
                entries = (await session.GetCacheEntryAsync(record.StrongFingerprint)).Success();
                XAssert.AreEqual(record.CasEntries, entries);
                XAssert.AreEqual(s_determinism[fromDeterminism].EffectiveGuid, entries.Determinism.EffectiveGuid);
            }

            await CloseSessionAsync(session, testSessionId);
            await ShutdownCacheAsync(cache, testCacheId);
        }

        [Theory]
        [InlineData(Determinism_None, Determinism_Tool, false)]
        [InlineData(Determinism_None, Determinism_Tool, true)]
        [InlineData(Determinism_None, Determinism_Cache1, false)]
        [InlineData(Determinism_None, Determinism_Cache1, true)]
        [InlineData(Determinism_Cache1, Determinism_None, false)]
        [InlineData(Determinism_Cache1, Determinism_None, true)]
        [InlineData(Determinism_Cache1, Determinism_Cache2, false)]
        [InlineData(Determinism_Cache1, Determinism_Cache2, true)]
        [InlineData(Determinism_Cache1, Determinism_Tool, false)]
        [InlineData(Determinism_Cache1, Determinism_Tool, true)]
        [InlineData(Determinism_Cache2, Determinism_Cache1, false)]
        [InlineData(Determinism_Cache2, Determinism_Cache1, true)]
        [InlineData(Determinism_Tool, Determinism_None, false)]
        [InlineData(Determinism_Tool, Determinism_None, true)]
        [InlineData(Determinism_Tool, Determinism_Cache1, false)]
        [InlineData(Determinism_Tool, Determinism_Cache1, true)]
        [InlineData(Determinism_Tool, Determinism_Tool, false)]
        [InlineData(Determinism_Tool, Determinism_Tool, true)]
        public async Task CasEntriesReplacedOnMissingContent(int fromDeterminism, int toDeterminism, bool differentCasEntries)
        {
            string testName = I($"ReplacedOnMissingContent{fromDeterminism}x{toDeterminism}{(differentCasEntries ? "Diff" : "Same")}");
            string testCacheId = MakeCacheId(testName);

            ICache cache = await CreateCacheAsync(testCacheId, strictMetadataCasCoupling: false);

            try
            {
                string testSessionId = "Session1-" + testCacheId;
                ICacheSession session = await CreateSessionAsync(cache, testSessionId);

                // We need at least 2 to make "differentCasEntries" work
                FullCacheRecord[] records =
                {
                    RandomHelpers.CreateRandomFullCacheRecord(session.CacheId, s_determinism[fromDeterminism]),
                    RandomHelpers.CreateRandomFullCacheRecord(session.CacheId, s_determinism[fromDeterminism])
                };
                foreach (var record in records)
                {
                    var addResult = await session.AddOrGetAsync(
                        record.StrongFingerprint.WeakFingerprint,
                        record.StrongFingerprint.CasElement,
                        record.StrongFingerprint.HashElement,
                        record.CasEntries).SuccessAsync();
                    XAssert.IsNull(addResult.Record);
                }

                await CloseSessionAsync(session, testSessionId);

                testSessionId = "Session2-" + testCacheId;
                session = await CreateSessionAsync(cache, testSessionId);

                // What we will do here is AddOrGet() a record with the determinism bit changed.
                for (int i = 0; i < records.Length; i++)
                {
                    var record = records[i];

                    var getResult = await session.GetCacheEntryAsync(record.StrongFingerprint).SuccessAsync();
                    XAssert.AreEqual(record.CasEntries.Determinism.EffectiveGuid, getResult.Determinism.EffectiveGuid);

                    // This gets the CasEntries we want
                    var recordsLength = records.Length;
                    CasEntries newEntries = records[(i + (differentCasEntries ? 1 : 0)) % recordsLength].CasEntries;

                    // Validate that the entry for the record is what we expect
                    var entries = (await session.GetCacheEntryAsync(record.StrongFingerprint)).Success();
                    XAssert.AreEqual(s_determinism[fromDeterminism].EffectiveGuid, entries.Determinism.EffectiveGuid);

                    // Now make a new record
                    var newRecord = (await session.AddOrGetAsync(
                        record.StrongFingerprint.WeakFingerprint,
                        record.StrongFingerprint.CasElement,
                        record.StrongFingerprint.HashElement,
                        new CasEntries(newEntries, s_determinism[toDeterminism]))).Success();

                    // The new record should be null because the old value should have
                    // been replaced with the new value in all cases (due to missing content).
                    XAssert.IsNull(newRecord.Record);

                    // Now, we will try to get the same record from the cache to validate the replacement
                    entries = (await session.GetCacheEntryAsync(record.StrongFingerprint)).Success();
                    XAssert.AreEqual(newEntries, entries);
                    XAssert.AreEqual(s_determinism[toDeterminism].EffectiveGuid, entries.Determinism.EffectiveGuid);
                }

                await CloseSessionAsync(session, testSessionId);
            }
            finally
            {
                await ShutdownCacheAsync(cache, testCacheId);
            }
        }
    }
}
