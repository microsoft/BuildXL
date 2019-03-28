// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using Xunit;

namespace BuildXL.Cache.Tests
{
    /// <summary>
    /// Tests for the vertical aggregating cache.
    /// </summary>
    /// <remarks>
    /// The public methods are marked as [Fact] to require derived classes to override as [Theory] and provide
    /// factories.
    /// </remarks>
    public abstract class VerticalAggregatorSharedTests : VerticalAggregatorBaseTests, IDisposable
    {
        /// <summary>
        /// After adding a fingerprint to an empty local, the FP information is available in both caches and is deterministic in the local cache.
        /// </summary>
        [Theory]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Self)]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Memory)]
        [InlineData(BackingStoreTestClass.Memory, BackingStoreTestClass.Self)]
        public virtual Task AddToEmptyCacheWithDeterministicRemote(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            return AddToEmptyCacheAsync(false, localCacheTestClass, remoteCacheTestClass);
        }

        /// <summary>
        /// Adds deterministic to the local cache and verifies it is deterministic in both caches.
        /// </summary>
        [Theory]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Self)]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Memory)]
        [InlineData(BackingStoreTestClass.Memory, BackingStoreTestClass.Self)]
        public virtual Task AddDeterministicContentToEmptyCache(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            return AddToEmptyCacheAsync(true, localCacheTestClass, remoteCacheTestClass);
        }

        /// <summary>
        /// A hit in a remote cache places the fp information into the local cache and marks it as deterministic before returning it.
        /// </summary>
        [Theory]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Self)]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Memory)]
        [InlineData(BackingStoreTestClass.Memory, BackingStoreTestClass.Self)]
        public override Task HitInDeterministicRemotePromotesToEmptyLocal(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            return base.HitInDeterministicRemotePromotesToEmptyLocal(localCacheTestClass, remoteCacheTestClass);
        }

        /// <summary>
        /// A local cache hit that is marked as non-deterministic is replaced with content from a remote cache.
        /// </summary>
        [Theory]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Self)]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Memory)]
        [InlineData(BackingStoreTestClass.Memory, BackingStoreTestClass.Self)]
        public override Task NonDeterministicContentReplaced(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            return base.NonDeterministicContentReplaced(localCacheTestClass, remoteCacheTestClass);
        }

        /// <summary>
        /// Adding a new fingerprint when the remote has a more deterministic output replaces the local value w/ the
        /// remote one.
        /// </summary>
        [Theory]
        [MemberData("BuildDeterminismMatrix")]
        public override Task AddingFpReplacedWithExistingRemote(
            BackingStoreTestClass localTestClass,
                                                                     BackingStoreTestClass remoteTestClass,
                                                                     CacheDeterminism initialDeterminismLocal,
                                                                     CacheDeterminism initialDeterminsimRemote,
                                                                     CacheDeterminism finalDeterminismLocal,
                                                                     CacheDeterminism finalDeterminismRemote)
        {
            return base.AddingFpReplacedWithExistingRemote(
                localTestClass,
                                                           remoteTestClass,
                                                           initialDeterminismLocal,
                                                           initialDeterminsimRemote,
                                                           finalDeterminismLocal,
                                                           finalDeterminismRemote);
        }

        protected static IEnumerable<object[]> BuildDeterminismMatrix()
        {
            for (int i = -1; i < determinisms.Length; i++)
            {
                int localIndex = Math.Max(0, i);
                CacheDeterminism startDeterminismLocal = determinisms[localIndex];
                CacheDeterminism startDeterminismRemote = determinisms[Math.Max(localIndex, 1)];
                CacheDeterminism endDetermismLocal = startDeterminismLocal.IsDeterministicTool || startDeterminismRemote.IsDeterministicTool ? CacheDeterminism.Tool : CacheDeterminism.ViaCache(RemoteReferenceGuild, CacheDeterminism.NeverExpires);
                CacheDeterminism endDeterminismRemote = startDeterminismRemote;

                yield return new object[] { BackingStoreTestClass.Self, BackingStoreTestClass.Self, startDeterminismLocal, startDeterminismRemote, endDetermismLocal, endDeterminismRemote };
                yield return new object[] { BackingStoreTestClass.Self, BackingStoreTestClass.Memory, startDeterminismLocal, startDeterminismRemote, endDetermismLocal, endDeterminismRemote };
                yield return new object[] { BackingStoreTestClass.Memory, BackingStoreTestClass.Self, startDeterminismLocal, startDeterminismRemote, endDetermismLocal, endDeterminismRemote };
            }
        }

        private static readonly CacheDeterminism[] determinisms = new CacheDeterminism[]
        {
            CacheDeterminism.None,
            CacheDeterminism.ViaCache(Guid.Parse("{E98CD792-5436-456B-92F5-63D635A3BFAC}"), CacheDeterminism.NeverExpires),
            CacheDeterminism.Tool
        };

        /// <summary>
        /// Fetching non-deterministic content from the local cache pushes content to empty remote cache,
        /// updates local cache to be deterministic, and returns determinstic content.
        /// </summary>
        [Theory]
        [MemberData("BuildDeterminismMatrix")]
        public override Task FetchingContentFromLocalCacheUpdatesRemoteCacheForDeterministicContentEmptyRemote(
            BackingStoreTestClass localTestClass,
                                                                                                                    BackingStoreTestClass remoteTestClass,
                                                                                                                    CacheDeterminism initialDeterminismLocal,
                                                                                                                    CacheDeterminism initialDeterminsimRemote,
                                                                                                                    CacheDeterminism finalDeterminismLocal,
                                                                                                                    CacheDeterminism finalDeterminismRemote)
        {
            return base.FetchingContentFromLocalCacheUpdatesRemoteCacheForDeterministicContentEmptyRemote(
                localTestClass,
                                                                                                                      remoteTestClass,
                                                                                                                      initialDeterminismLocal,
                                                                                                                      initialDeterminsimRemote,
                                                                                                                      finalDeterminismLocal,
                                                                                                                      finalDeterminismRemote);
        }

        /// <summary>
        /// Comin back online from the airplane scenario when both you and the remote have content.
        /// </summary>
        [Theory]
        [MemberData("BuildDeterminismMatrix")]
        public override Task FetchingContentFromLocalCacheUpdatesLocalCacheForDeterministicContentPopulatedRemote(
            BackingStoreTestClass localTestClass,
                                                                                                                        BackingStoreTestClass remoteTestClass,
                                                                                                                        CacheDeterminism initialDeterminismLocal,
                                                                                                                        CacheDeterminism initialDeterminismRemote,
                                                                                                                        CacheDeterminism finalDeterminismLocal,
                                                                                                                        CacheDeterminism finalDeterminismRemote)
        {
            return base.FetchingContentFromLocalCacheUpdatesLocalCacheForDeterministicContentPopulatedRemote(
                localTestClass,
                                                                                                                         remoteTestClass,
                                                                                                                         initialDeterminismLocal,
                                                                                                                         initialDeterminismRemote,
                                                                                                                         finalDeterminismLocal,
                                                                                                                         finalDeterminismRemote);
        }

        /// <summary>
        /// When cache hits happen in L1, the L2 may not see some or all of
        /// the cache hit requests (good thing) but that would prevent it from
        /// knowing of the use and being able to track the session, GC content,
        /// or LRU the data.  Thus, the aggregator can provide the L2 the set
        /// of fingerprints that were actually used during the session.
        ///
        /// Only of use for the cases where the L2 is not read-only and, of course,
        /// where the session is named (where such tracking is even possible).
        /// </summary>
        [Theory]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Self)]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Memory)]
        [InlineData(BackingStoreTestClass.Memory, BackingStoreTestClass.Self)]
        public override Task SessionRecordTransferTest(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            return base.SessionRecordTransferTest(localCacheTestClass, remoteCacheTestClass);
        }

        [Theory]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Self)]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Memory)]
        [InlineData(BackingStoreTestClass.Memory, BackingStoreTestClass.Self)]
        public override Task EnsureSentinelReturnedDuringEnumeration(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            return base.EnsureSentinelReturnedDuringEnumeration(localCacheTestClass, remoteCacheTestClass);
        }

        [Theory]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Self)]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Memory)]
        [InlineData(BackingStoreTestClass.Memory, BackingStoreTestClass.Self)]
        public override Task AggreatorReturnsRemoteCacheGuid(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            return base.AggreatorReturnsRemoteCacheGuid(localCacheTestClass, remoteCacheTestClass);
        }

        // Remote is readonly, local is writeable.
        [Theory]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Self)]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Memory)]
        [InlineData(BackingStoreTestClass.Memory, BackingStoreTestClass.Self)]
        public override Task ReadOnlyRemoteIsNotUpdated(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            return base.ReadOnlyRemoteIsNotUpdated(localCacheTestClass, remoteCacheTestClass);
        }

        [Theory]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Self)]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Memory)]
        [InlineData(BackingStoreTestClass.Memory, BackingStoreTestClass.Self)]
        public override Task ReadOnlyRemoteIsNotUpdatedForLocalHit(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            return base.ReadOnlyRemoteIsNotUpdatedForLocalHit(localCacheTestClass, remoteCacheTestClass);
        }

        [Theory]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Self, true)]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Memory, true)]
        [InlineData(BackingStoreTestClass.Memory, BackingStoreTestClass.Self, true)]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Self, false)]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Memory, false)]
        [InlineData(BackingStoreTestClass.Memory, BackingStoreTestClass.Self, false)]
        public override Task CacheMiss(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass, bool remoteReadOnly)
        {
            return base.CacheMiss(localCacheTestClass, remoteCacheTestClass, remoteReadOnly);
        }

        // Writing to protect against regression for bug fix.
        // Not making very generic as we can delete when SinglePhaseDeterministic goes away. Hopefully soon.
        [Theory]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Self)]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Memory)]
        [InlineData(BackingStoreTestClass.Memory, BackingStoreTestClass.Self)]
        public override Task ReadOnlyRemoteSinglePhaseRemoteAdd(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            return base.ReadOnlyRemoteSinglePhaseRemoteAdd(localCacheTestClass, remoteCacheTestClass);
        }

        [Theory]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Self)]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Memory)]
        [InlineData(BackingStoreTestClass.Memory, BackingStoreTestClass.Self)]
        public override Task SinglePhaseDeterminismStaysSinglePhase(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            return base.SinglePhaseDeterminismStaysSinglePhase(localCacheTestClass, remoteCacheTestClass);
        }

        [Theory]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Self)]
        [InlineData(BackingStoreTestClass.Self, BackingStoreTestClass.Memory)]
        [InlineData(BackingStoreTestClass.Memory, BackingStoreTestClass.Self)]
        public override Task RecordsAreIncooperated(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            return base.RecordsAreIncooperated(localCacheTestClass, remoteCacheTestClass);
        }

        /// <summary>
        /// Adding a new fingerprint when the remote has a more deterministic output replaces the local value w/ the
        /// remote one.
        /// </summary>
        [Theory]
        [MemberData("BuildDeterminismMatrix")]
        public override Task AddingFpReplacedWithExistingRORemote(
            BackingStoreTestClass localTestClass,
                                                                     BackingStoreTestClass remoteTestClass,
                                                                     CacheDeterminism initialDeterminismLocal,
                                                                     CacheDeterminism initialDeterminsimRemote,
                                                                     CacheDeterminism finalDeterminismLocal,
                                                                     CacheDeterminism finalDeterminismRemote)
        {
            return base.AddingFpReplacedWithExistingRORemote(
                localTestClass,
                                                                      remoteTestClass,
                                                                      initialDeterminismLocal,
                                                                      initialDeterminsimRemote,
                                                                      finalDeterminismLocal,
                                                                      finalDeterminismRemote);
        }
    }
}
