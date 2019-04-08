// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces.Test;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.Tests;
using BuildXL.Cache.VerticalAggregator;
using BuildXL.Storage;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Cache.VerticalAggregator.Test
{
    /// <summary>
    /// Runs the vertical aggregator tests with the in memory caches.
    /// </summary>
    /// This class will also be where more complex aggregator tests are written due to
    /// the performance advantages of using the memory cache.
    public class VerticalAggregatorTests : VerticalAggregatorBaseTests
    {
        protected override Type ReferenceType => typeof(TestInMemory);

        protected override Type TestType => typeof(TestInMemory);

        #region standard tests

        /// <summary>
        /// After adding a fingerprint to an empty local, the FP information is available in both caches and is deterministic in the local cache.
        /// </summary>
        [Fact]
        public Task AddToEmptyCacheWithDeterministicRemote()
        {
            return AddToEmptyCacheAsync(false, BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        /// <summary>
        /// Adds deterministic to the local cache and verifies it is deterministic in both caches.
        /// </summary>
        [Fact]
        public Task AddDeterministicContentToEmptyCache()
        {
            return AddToEmptyCacheAsync(true, BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        /// <summary>
        /// A hit in a remote cache places the fp information into the local cache and marks it as deterministic before returning it.
        /// </summary>
        [Fact]
        public Task HitInDeterministicRemotePromotesToEmptyLocal()
        {
            return this.HitInDeterministicRemotePromotesToEmptyLocal(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        /// <summary>
        /// A local cache hit that is marked as non-deterministic is replaced with content from a remote cache.
        /// </summary>
        [Fact]
        public Task NonDeterministicContentReplaced()
        {
            return this.NonDeterministicContentReplaced(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
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
        [Fact]
        public Task SessionRecordTransferTest()
        {
            return this.SessionRecordTransferTest(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        [Fact]
        public Task EnsureSentinelReturnedDuringEnumeration()
        {
            return this.EnsureSentinelReturnedDuringEnumeration(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        [Fact]
        public Task AggreatorReturnsRemoteCacheGuid()
        {
            return this.AggreatorReturnsRemoteCacheGuid(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        // Remote is readonly, local is writeable.
        [Fact]
        public Task ReadOnlyRemoteIsNotUpdated()
        {
            return this.ReadOnlyRemoteIsNotUpdated(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        [Fact]
        public Task ReadOnlyRemoteIsNotUpdatedForLocalHit()
        {
            return this.ReadOnlyRemoteIsNotUpdatedForLocalHit(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task CacheMiss(bool remoteReadOnly)
        {
            return this.CacheMiss(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory, remoteReadOnly);
        }

        // Writing to protect against regression for bug fix.
        // Not making very generic as we can delete when SinglePhaseDeterministic goes away. Hopefully soon.
        [Fact]
        public Task ReadOnlyRemoteSinglePhaseRemoteAdd()
        {
            return this.ReadOnlyRemoteSinglePhaseRemoteAdd(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        [Fact]
        public Task SinglePhaseDeterminismStaysSinglePhase()
        {
            return this.SinglePhaseDeterminismStaysSinglePhase(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        [Fact]
        public Task RecordsAreIncooperated()
        {
            return this.RecordsAreIncooperated(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
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

        #endregion
        public VerticalAggregatorTests()
        {
            AddSessionFailures();
            AddSessionExceptions();
        }

        private void AddSessionExceptions()
        {
            m_sessionExceptionInducers.Add(SessionAPIs.AddOrGetAsyncCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.AddOrGetAsyncCallback = (WeakFingerprintHash weak, CasHash casElement, Hash hashElement, CasEntries hashes, UrgencyHint urgencyHint, Guid activityId, ICacheSession wrappedSession) =>
                {
                    // Any error should work.
                    throw new TestException();
                };
                return 0;
            });

            m_sessionExceptionInducers.Add(SessionAPIs.AddToCasAsyncCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.AddToCasAsyncCallback = (Stream filestream, CasHash? casHash, UrgencyHint urgencyHint, Guid activityId, ICacheSession wrappedSession) =>
                {
                    // Any error should work.
                    throw new TestException();
                };
                return 0;
            });

            m_sessionExceptionInducers.Add(SessionAPIs.AddToCasFilenameAsyncCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.AddToCasFilenameAsyncCallback = (string filestream, FileState state, CasHash? hash, UrgencyHint urgencyHint, Guid activityId, ICacheSession wrappedSession) =>
                {
                    // Any error should work.
                    throw new TestException();
                };
                return 0;
            });

            m_sessionExceptionInducers.Add(SessionAPIs.EnumerateStrongFingerprintsCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.EnumerateStrongFingerprintsCallback = (WeakFingerprintHash weak, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    throw new TestException();
                };
                return 0;
            });

            m_sessionExceptionInducers.Add(SessionAPIs.GetCacheEntryAsyncCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.GetCacheEntryAsyncCallback = (StrongFingerprint strong, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    throw new TestException();
                };
                return 0;
            });

            m_sessionExceptionInducers.Add(SessionAPIs.GetStreamAsyncCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.GetStreamAsyncCallback = (CasHash hash, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    throw new TestException();
                };
                return 0;
            });

            m_sessionExceptionInducers.Add(SessionAPIs.PinToCasAsyncCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.PinToCasAsyncCallback = (CasHash hash, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    throw new TestException();
                };
                return 0;
            });

            m_sessionExceptionInducers.Add(SessionAPIs.PinToCasMultipleAsyncCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.PinToCasMultipleAsyncCallback = (CasEntries hashes, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    throw new TestException();
                };
                return 0;
            });

            m_sessionExceptionInducers.Add(SessionAPIs.ProduceFileAsyncCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.ProduceFileAsyncCallback = (CasHash hash, string filename, FileState fileState, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    throw new TestException();
                };
                return 0;
            });

            m_readOnlySessionExceptionInducers.Add(SessionAPIs.EnumerateStrongFingerprintsCallback, (CallbackCacheReadOnlySessionWrapper targetSession) =>
            {
                targetSession.EnumerateStrongFingerprintsCallback = (WeakFingerprintHash weak, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    throw new TestException();
                };
                return 0;
            });

            m_readOnlySessionExceptionInducers.Add(SessionAPIs.GetCacheEntryAsyncCallback, (CallbackCacheReadOnlySessionWrapper targetSession) =>
            {
                targetSession.GetCacheEntryAsyncCallback = (StrongFingerprint strong, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    throw new TestException();
                };
                return 0;
            });

            m_readOnlySessionExceptionInducers.Add(SessionAPIs.GetStreamAsyncCallback, (CallbackCacheReadOnlySessionWrapper targetSession) =>
            {
                targetSession.GetStreamAsyncCallback = (CasHash hash, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    throw new TestException();
                };
                return 0;
            });

            m_readOnlySessionExceptionInducers.Add(SessionAPIs.PinToCasAsyncCallback, (CallbackCacheReadOnlySessionWrapper targetSession) =>
            {
                targetSession.PinToCasAsyncCallback = (CasHash hash, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    throw new TestException();
                };
                return 0;
            });

            m_readOnlySessionExceptionInducers.Add(SessionAPIs.PinToCasMultipleAsyncCallback, (CallbackCacheReadOnlySessionWrapper targetSession) =>
            {
                targetSession.PinToCasMultipleAsyncCallback = (CasEntries hashes, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    throw new TestException();
                };
                return 0;
            });

            m_readOnlySessionExceptionInducers.Add(SessionAPIs.ProduceFileAsyncCallback, (CallbackCacheReadOnlySessionWrapper targetSession) =>
            {
                targetSession.ProduceFileAsyncCallback = (CasHash hash, string filename, FileState fileState, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    throw new TestException();
                };
                return 0;
            });
        }

        private void AddSessionFailures()
        {
            m_sessionFailureInducers.Add(SessionAPIs.AddOrGetAsyncCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.AddOrGetAsyncCallback = (WeakFingerprintHash weak, CasHash casElement, Hash hashElement, CasEntries hashes, UrgencyHint urgencyHint, Guid activityId, ICacheSession wrappedSession) =>
                {
                    // Any error should work.
                    return Task.FromResult(new Possible<FullCacheRecordWithDeterminism, Failure>(new TestInducedFailure("(AddOrGetAsync)")));
                };
                return 0;
            });

            m_sessionFailureInducers.Add(SessionAPIs.AddToCasAsyncCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.AddToCasAsyncCallback = (Stream filestream, CasHash? casHash, UrgencyHint urgencyHint, Guid activityId, ICacheSession wrappedSession) =>
                {
                    // Any error should work.
                    return Task.FromResult(new Possible<CasHash, Failure>(new TestInducedFailure("(AddToCasAsync)")));
                };
                return 0;
            });

            m_sessionFailureInducers.Add(SessionAPIs.AddToCasFilenameAsyncCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.AddToCasFilenameAsyncCallback = (string filestream, FileState state, CasHash? hash, UrgencyHint urgencyHint, Guid activityId, ICacheSession wrappedSession) =>
                {
                    // Any error should work.
                    return Task.FromResult(new Possible<CasHash, Failure>(new TestInducedFailure("(AddToCasAsyncFilename)")));
                };
                return 0;
            });

            m_sessionFailureInducers.Add(SessionAPIs.EnumerateStrongFingerprintsCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.EnumerateStrongFingerprintsCallback = (WeakFingerprintHash weak, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    return new Task<Possible<StrongFingerprint, Failure>>[] { Task.FromResult(new Possible<StrongFingerprint, Failure>(new TestInducedFailure("(EnumerateStrongFingerprints)"))) };
                };
                return 0;
            });

            m_sessionFailureInducers.Add(SessionAPIs.GetCacheEntryAsyncCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.GetCacheEntryAsyncCallback = (StrongFingerprint strong, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    return Task.FromResult(new Possible<CasEntries, Failure>(new TestInducedFailure("GetCacheEntry")));
                };
                return 0;
            });

            m_sessionFailureInducers.Add(SessionAPIs.GetStreamAsyncCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.GetStreamAsyncCallback = (CasHash hash, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    return Task.FromResult(new Possible<Stream, Failure>(new TestInducedFailure("(GetStreamAsync)")));
                };
                return 0;
            });

            m_sessionFailureInducers.Add(SessionAPIs.PinToCasAsyncCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.PinToCasAsyncCallback = (CasHash hash, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    return Task.FromResult(new Possible<string, Failure>(new TestInducedFailure("(PinToCas)")));
                };
                return 0;
            });

            m_sessionFailureInducers.Add(SessionAPIs.PinToCasMultipleAsyncCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.PinToCasMultipleAsyncCallback = (CasEntries hashes, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    return Task.FromResult(new Possible<string, Failure>[] { new TestInducedFailure("(PinToCasMultiple)") });
                };
                return 0;
            });

            m_sessionFailureInducers.Add(SessionAPIs.ProduceFileAsyncCallback, (CallbackCacheSessionWrapper targetSession) =>
            {
                targetSession.ProduceFileAsyncCallback = (CasHash hash, string filename, FileState fileState, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    return Task.FromResult(new Possible<string, Failure>(new TestInducedFailure("(ProduceFile)")));
                };
                return 0;
            });

            m_readOnlySessionFailureInducers.Add(SessionAPIs.EnumerateStrongFingerprintsCallback, (CallbackCacheReadOnlySessionWrapper targetSession) =>
            {
                targetSession.EnumerateStrongFingerprintsCallback = (WeakFingerprintHash weak, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    return new Task<Possible<StrongFingerprint, Failure>>[] { Task.FromResult(new Possible<StrongFingerprint, Failure>(new TestInducedFailure("(EnumerateStrongFingerprints)"))) };
                };
                return 0;
            });

            m_readOnlySessionFailureInducers.Add(SessionAPIs.GetCacheEntryAsyncCallback, (CallbackCacheReadOnlySessionWrapper targetSession) =>
            {
                targetSession.GetCacheEntryAsyncCallback = (StrongFingerprint strong, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    return Task.FromResult(new Possible<CasEntries, Failure>(new TestInducedFailure("GetCacheEntry")));
                };
                return 0;
            });

            m_readOnlySessionFailureInducers.Add(SessionAPIs.GetStreamAsyncCallback, (CallbackCacheReadOnlySessionWrapper targetSession) =>
            {
                targetSession.GetStreamAsyncCallback = (CasHash hash, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    return Task.FromResult(new Possible<Stream, Failure>(new TestInducedFailure("(GetStreamAsync)")));
                };
                return 0;
            });

            m_readOnlySessionFailureInducers.Add(SessionAPIs.PinToCasAsyncCallback, (CallbackCacheReadOnlySessionWrapper targetSession) =>
            {
                targetSession.PinToCasAsyncCallback = (CasHash hash, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    return Task.FromResult(new Possible<string, Failure>(new TestInducedFailure("(PinToCas)")));
                };
                return 0;
            });

            m_readOnlySessionFailureInducers.Add(SessionAPIs.PinToCasMultipleAsyncCallback, (CallbackCacheReadOnlySessionWrapper targetSession) =>
            {
                targetSession.PinToCasMultipleAsyncCallback = (CasEntries hashes, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    return Task.FromResult(new Possible<string, Failure>[] { new TestInducedFailure("(PinToCasMultiple)") });
                };
                return 0;
            });

            m_readOnlySessionFailureInducers.Add(SessionAPIs.ProduceFileAsyncCallback, (CallbackCacheReadOnlySessionWrapper targetSession) =>
            {
                targetSession.ProduceFileAsyncCallback = (CasHash hash, string filename, FileState fileState, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
                {
                    // Any error should work.
                    return Task.FromResult(new Possible<string, Failure>(new TestInducedFailure("(ProduceFile)")));
                };
                return 0;
            });
        }

        private readonly Dictionary<SessionAPIs, Func<CallbackCacheSessionWrapper, uint>> m_sessionFailureInducers = new Dictionary<SessionAPIs, Func<CallbackCacheSessionWrapper, uint>>();
        private readonly Dictionary<SessionAPIs, Func<CallbackCacheSessionWrapper, uint>> m_sessionExceptionInducers = new Dictionary<SessionAPIs, Func<CallbackCacheSessionWrapper, uint>>();
        private readonly Dictionary<SessionAPIs, Func<CallbackCacheReadOnlySessionWrapper, uint>> m_readOnlySessionFailureInducers = new Dictionary<SessionAPIs, Func<CallbackCacheReadOnlySessionWrapper, uint>>();
        private readonly Dictionary<SessionAPIs, Func<CallbackCacheReadOnlySessionWrapper, uint>> m_readOnlySessionExceptionInducers = new Dictionary<SessionAPIs, Func<CallbackCacheReadOnlySessionWrapper, uint>>();

        private void InduceFailures(SessionAPIs apisToFail, CallbackCacheSessionWrapper session)
        {
            foreach (SessionAPIs api in Enum.GetValues(typeof(SessionAPIs)))
            {
                if ((api & apisToFail) != 0)
                {
                    m_sessionFailureInducers[api].Invoke(session);
                }
            }
        }

        private void InduceExceptions(SessionAPIs apisToFail, CallbackCacheSessionWrapper session)
        {
            foreach (SessionAPIs api in Enum.GetValues(typeof(SessionAPIs)))
            {
                if ((api & apisToFail) != 0)
                {
                    m_sessionExceptionInducers[api].Invoke(session);
                }
            }
        }

        private void InduceExceptions(SessionAPIs apisToFail, CallbackCacheReadOnlySessionWrapper session)
        {
            foreach (SessionAPIs api in Enum.GetValues(typeof(SessionAPIs)))
            {
                if ((api & apisToFail) != 0 && m_readOnlySessionExceptionInducers.ContainsKey(api))
                {
                    m_readOnlySessionExceptionInducers[api].Invoke(session);
                }
            }
        }

        [Flags]
        public enum SessionAPIs
        {
            AddOrGetAsyncCallback = 1 << 1,
            AddToCasAsyncCallback = 1 << 2,
            AddToCasFilenameAsyncCallback = 1 << 3,
            CacheIdCallback = 1 << 4,
            CacheSessionIdCallback = 1 << 5,
            CloseAsyncCallback = 1 << 6,
            EnumerateStrongFingerprintsCallback = 1 << 7,
            GetCacheEntryAsyncCallback = 1 << 8,
            GetStatisticsAsyncCallback = 1 << 9,
            GetStreamAsyncCallback = 1 << 10,
            IsClosedCallback = 1 << 11,
            PinToCasAsyncCallback = 1 << 12,
            PinToCasMultipleAsyncCallback = 1 << 13,
            ProduceFileAsyncCallback = 1 << 14,
            StrictMetadataCasCouplingCallback = 1 << 15
        }

        public static IEnumerable<object[]> BuildErrorOnAddMatrix()
        {
            yield return new object[] { SessionAPIs.GetCacheEntryAsyncCallback, true };
            yield return new object[] { SessionAPIs.AddToCasFilenameAsyncCallback, true };
            yield return new object[] { SessionAPIs.PinToCasMultipleAsyncCallback, true };
            yield return new object[] { SessionAPIs.AddToCasAsyncCallback, false };
            yield return new object[] { SessionAPIs.AddOrGetAsyncCallback, false };
        }

        [Theory]
        [MemberData(nameof(BuildErrorOnAddMatrix))]
        public async Task RemoteErrorOnAddStillInLocal(SessionAPIs apisToFail, bool remoteSuccess)
        {
            string testCacheId = "ErrorInducingRemote";
            ICache testCache = await InitializeCacheAsync(VerticalAggregatorDisconnectTests.NewWrappedRemoteCache(testCacheId, false, false)).SuccessAsync();
            VerticalAggregator.VerticalCacheAggregator vertCache = VerticalAggregatorDisconnectTests.UnwrapVerticalCache(testCache);

            ICacheSession session = await testCache.CreateSessionAsync().SuccessAsync();
            CallbackCacheSessionWrapper remoteSessionWrapper = VerticalAggregatorDisconnectTests.UnwrapRemoteSession(session);

            InduceFailures(apisToFail, remoteSessionWrapper);

            // Run a fake build.
            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(session, "Test Pip");

            // Ok, so the local cache should have an entry, and the remote should have the same entry as the build
            // will have simply skipped an optimization step before adding the content.
            await ValidateItemsInCacheAsync(
                vertCache.LocalCache,
                                cacheRecord.StrongFingerprint.WeakFingerprint,
                                new List<CasHash>(cacheRecord.CasEntries),
                                remoteSuccess ? CacheDeterminism.ViaCache(vertCache.RemoteCache.CacheGuid, CacheDeterminism.NeverExpires) : CacheDeterminism.None,
                                cacheRecord.StrongFingerprint.CasElement,
                                vertCache.LocalCache.CacheId,
                                1);

            await ValidateItemsInCacheAsync(
                vertCache.RemoteCache,
                                cacheRecord.StrongFingerprint.WeakFingerprint,
                                new List<CasHash>(cacheRecord.CasEntries),
                                remoteSuccess ? CacheDeterminism.ViaCache(vertCache.RemoteCache.CacheGuid, CacheDeterminism.NeverExpires) : CacheDeterminism.None,
                                cacheRecord.StrongFingerprint.CasElement,
                                vertCache.RemoteCache.CacheId,
                                remoteSuccess ? 1 : 0);

            await session.CloseAsync().SuccessAsync();
            await testCache.ShutdownAsync().SuccessAsync();
        }

        [Theory]
        [MemberData(nameof(BuildErrorOnAddMatrix))]
        public async Task RemoteExceptionOnAdd(SessionAPIs apisToFail, bool remoteSuccess)
        {
            string testCacheId = "ErrorInducingRemote";
            ICache testCache = await InitializeCacheAsync(VerticalAggregatorDisconnectTests.NewWrappedRemoteCache(testCacheId, false, false)).SuccessAsync();
            VerticalAggregator.VerticalCacheAggregator vertCache = VerticalAggregatorDisconnectTests.UnwrapVerticalCache(testCache);

            ICacheSession session = await testCache.CreateSessionAsync().SuccessAsync();
            CallbackCacheSessionWrapper remoteSessionWrapper = VerticalAggregatorDisconnectTests.UnwrapRemoteSession(session);

            CallbackCacheWrapper callbackCache = vertCache.RemoteCache as CallbackCacheWrapper;
            callbackCache.CreateReadOnlySessionAsyncCallback = async (ICache cache) =>
            {
                var wrappedSession = await cache.CreateReadOnlySessionAsync();
                if (!wrappedSession.Succeeded)
                {
                    return wrappedSession;
                }

                CallbackCacheReadOnlySessionWrapper retWrapper = new CallbackCacheReadOnlySessionWrapper(wrappedSession.Result);

                InduceExceptions(apisToFail, retWrapper);
                return wrappedSession;
            };

            callbackCache.CreateSessionAsyncCallback = async (ICache cache) =>
            {
                var wrappedSession = await cache.CreateSessionAsync();
                if (!wrappedSession.Succeeded)
                {
                    return wrappedSession;
                }

                CallbackCacheSessionWrapper retWrapper = new CallbackCacheSessionWrapper(wrappedSession.Result);

                InduceExceptions(apisToFail, retWrapper);
                return wrappedSession;
            };

            InduceExceptions(apisToFail, remoteSessionWrapper);

            FullCacheRecord cacheRecord = null;

            try
            {
                // Run a fake build.
                cacheRecord = await FakeBuild.DoPipAsync(session, "Test Pip");
            }
            catch (TestException)
            {
            }

            await session.CloseAsync().SuccessAsync();
            await testCache.ShutdownAsync().SuccessAsync();
        }

        [Theory]
        [MemberData(nameof(BuildErrorOnAddMatrix))]
        public async Task LocalExceptionOnAdd(SessionAPIs apisToFail, bool remoteSuccess)
        {
            string testCacheId = "ErrorInducingRemote";
            ICache testCache = await InitializeCacheAsync(VerticalAggregatorDisconnectTests.NewWrappedLocalCache(testCacheId, false, false)).SuccessAsync();
            VerticalAggregator.VerticalCacheAggregator vertCache = VerticalAggregatorDisconnectTests.UnwrapVerticalCache(testCache);

            ICacheSession session = await testCache.CreateSessionAsync().SuccessAsync();
            CallbackCacheSessionWrapper localSessionWrapper = VerticalAggregatorDisconnectTests.UnwrapLocalSession(session);

            CallbackCacheWrapper callbackCache = vertCache.LocalCache as CallbackCacheWrapper;
            callbackCache.CreateReadOnlySessionAsyncCallback = async (ICache cache) =>
            {
                var wrappedSession = await cache.CreateReadOnlySessionAsync();
                if (!wrappedSession.Succeeded)
                {
                    return wrappedSession;
                }

                CallbackCacheReadOnlySessionWrapper retWrapper = new CallbackCacheReadOnlySessionWrapper(wrappedSession.Result);

                InduceExceptions(apisToFail, retWrapper);
                return wrappedSession;
            };

            callbackCache.CreateSessionAsyncCallback = async (ICache cache) =>
            {
                var wrappedSession = await cache.CreateSessionAsync();
                if (!wrappedSession.Succeeded)
                {
                    return wrappedSession;
                }

                CallbackCacheSessionWrapper retWrapper = new CallbackCacheSessionWrapper(wrappedSession.Result);

                InduceExceptions(apisToFail, retWrapper);
                return wrappedSession;
            };

            InduceExceptions(apisToFail, localSessionWrapper);

            FullCacheRecord cacheRecord = null;

            try
            {
                // Run a fake build.
                cacheRecord = await FakeBuild.DoPipAsync(session, "Test Pip");
            }
            catch (TestException)
            {
            }

            await session.CloseAsync().SuccessAsync();
            await testCache.ShutdownAsync().SuccessAsync();
        }

        [Theory]
        [InlineData(SessionAPIs.GetCacheEntryAsyncCallback)]
        [InlineData(SessionAPIs.GetCacheEntryAsyncCallback)]
        [InlineData(SessionAPIs.AddToCasFilenameAsyncCallback)]
        [InlineData(SessionAPIs.PinToCasAsyncCallback)]
        [InlineData(SessionAPIs.PinToCasMultipleAsyncCallback)]
        [InlineData(SessionAPIs.AddToCasAsyncCallback)]
        [InlineData(SessionAPIs.AddOrGetAsyncCallback)]
        public async Task RemoteErrorOnGetCacheEntryCacheMiss(SessionAPIs apisToFail)
        {
            string testCacheId = "ErrorInducingRemote";
            ICache testCache = await InitializeCacheAsync(VerticalAggregatorDisconnectTests.NewWrappedRemoteCache(testCacheId, false, false)).SuccessAsync();
            VerticalAggregator.VerticalCacheAggregator vertCache = VerticalAggregatorDisconnectTests.UnwrapVerticalCache(testCache);

            ICacheSession session = await testCache.CreateSessionAsync().SuccessAsync();
            CallbackCacheSessionWrapper remoteSessionWrapper = VerticalAggregatorDisconnectTests.UnwrapRemoteSession(session);

            InduceFailures(apisToFail, remoteSessionWrapper);

            WeakFingerprintHash weak = new WeakFingerprintHash(FingerprintUtilities.Hash("Weak").ToByteArray());
            CasHash casEntry = new CasHash(FingerprintUtilities.Hash("CAS").ToByteArray());
            Hash hashElement = new Hash(FingerprintUtilities.Hash("hash"));

            StrongFingerprint sfp = new StrongFingerprint(weak, casEntry, hashElement, "fake");

            var result = await session.GetCacheEntryAsync(sfp);
            XAssert.IsFalse(result.Succeeded, "GetCacheEntry should have failed");
            XAssert.IsTrue(result.Failure is NoMatchingFingerprintFailure, "Failure was " + result.Failure.Describe());
        }

        public static IEnumerable<object[]> BuildErrorOnGetMatrix()
        {
            yield return new object[] { SessionAPIs.GetCacheEntryAsyncCallback, true };
            yield return new object[] { SessionAPIs.AddToCasFilenameAsyncCallback, false };
            yield return new object[] { SessionAPIs.PinToCasMultipleAsyncCallback, false };
            yield return new object[] { SessionAPIs.AddToCasAsyncCallback, true };
            yield return new object[] { SessionAPIs.AddOrGetAsyncCallback, true };
            yield return new object[] { SessionAPIs.GetStreamAsyncCallback, false };
        }

        [Theory]
        [MemberData(nameof(BuildErrorOnGetMatrix))]
        public async Task RemoteErrorOnGetCacheEntryCacheHitLocal(SessionAPIs apisToFail, bool effectsOutcome)
        {
            string testCacheId = "ErrorInducingRemote";
            ICache testCache = await InitializeCacheAsync(VerticalAggregatorDisconnectTests.NewWrappedRemoteCache(testCacheId, false, false)).SuccessAsync();
            VerticalAggregator.VerticalCacheAggregator vertCache = VerticalAggregatorDisconnectTests.UnwrapVerticalCache(testCache);

            ICacheSession session = await testCache.CreateSessionAsync().SuccessAsync();
            CallbackCacheSessionWrapper remoteSessionWrapper = VerticalAggregatorDisconnectTests.UnwrapRemoteSession(session);

            ICacheSession localSession = await vertCache.LocalCache.CreateSessionAsync().SuccessAsync();

            InduceFailures(apisToFail, remoteSessionWrapper);
            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(localSession, "Test Pip");

            var result = await session.GetCacheEntryAsync(cacheRecord.StrongFingerprint);
            XAssert.IsTrue(result.Succeeded, "GetCacheEntry should have worked, error {0}", result.Succeeded ? null : result.Failure.Describe());
            XAssert.AreEqual(effectsOutcome ? CacheDeterminism.None : CacheDeterminism.ViaCache(vertCache.RemoteCache.CacheGuid, CacheDeterminism.NeverExpires), result.Result.Determinism, "Determinism bits did not match");
        }

        [Theory]
        [MemberData(nameof(BuildErrorOnGetMatrix))]
        public async Task RemoteExceptionOnGetFromRemote(SessionAPIs apisToFail, bool remoteSuccess)
        {
            string testCacheId = "ErrorInducingRemote";
            ICache testCache = await InitializeCacheAsync(VerticalAggregatorDisconnectTests.NewWrappedRemoteCache(testCacheId, false, false)).SuccessAsync();
            VerticalAggregator.VerticalCacheAggregator vertCache = VerticalAggregatorDisconnectTests.UnwrapVerticalCache(testCache);

            ICacheSession remoteSession = await vertCache.RemoteCache.CreateSessionAsync().SuccessAsync();

            CallbackCacheWrapper callbackCache = (CallbackCacheWrapper)vertCache.RemoteCache;
            callbackCache.CreateSessionAsyncCallback = async (ICache cache) =>
            {
                var sessionToWrap = await cache.CreateSessionAsync().SuccessAsync();
                CallbackCacheSessionWrapper wrappedSession = new CallbackCacheSessionWrapper(sessionToWrap);
                InduceExceptions(apisToFail, wrappedSession);
                return wrappedSession;
            };

            callbackCache.CreateReadOnlySessionAsyncCallback = async (ICache cache) =>
            {
                var sessionToWrap = await cache.CreateReadOnlySessionAsync().SuccessAsync();
                CallbackCacheReadOnlySessionWrapper wrappedSession = new CallbackCacheReadOnlySessionWrapper(sessionToWrap);
                InduceExceptions(apisToFail, wrappedSession);
                return wrappedSession;
            };

            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(remoteSession, "Test Pip");

            try
            {
                await ValidateItemsInCacheAsync(
                    vertCache,
                    cacheRecord.StrongFingerprint.WeakFingerprint,
                    new List<CasHash>(cacheRecord.CasEntries),
                    CacheDeterminism.ViaCache(vertCache.RemoteCache.CacheGuid, CacheDeterminism.NeverExpires),
                    cacheRecord.StrongFingerprint.CasElement,
                    vertCache.RemoteCache.CacheId,
                    1);
            }
            catch (TestException)
            {
            }

            await testCache.ShutdownAsync().SuccessAsync();
        }

        [Fact]
        public async Task DisconnectedRemoteAddOrGet()
        {
            string testCacheId = "ErrorInducingRemote";
            ICache testCache = await InitializeCacheAsync(VerticalAggregatorDisconnectTests.NewWrappedRemoteCache(testCacheId, false, false)).SuccessAsync();
            VerticalAggregator.VerticalCacheAggregator vertCache = VerticalAggregatorDisconnectTests.UnwrapVerticalCache(testCache);

            ICacheSession session = await testCache.CreateSessionAsync().SuccessAsync();
            CallbackCacheSessionWrapper remoteSessionWrapper = VerticalAggregatorDisconnectTests.UnwrapRemoteSession(session);

            ICacheSession localSession = await vertCache.LocalCache.CreateSessionAsync().SuccessAsync();

            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(localSession, "Test Pip");

            var result = await session.GetCacheEntryAsync(cacheRecord.StrongFingerprint);
            XAssert.IsTrue(result.Succeeded, "GetCacheEntry should have worked, error {0}", result.Succeeded ? null : result.Failure.Describe());
        }

        /// <nodoc/>
        [Fact]
        public async Task CorruptionRecovery()
        {
            const string TestName = "CorruptionRecovery";
            string testCacheId = MakeCacheId(TestName);
            ICache testCache = await NewCacheAsync(testCacheId, BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
            VerticalAggregator.VerticalCacheAggregator cache = testCache as VerticalAggregator.VerticalCacheAggregator;
            XAssert.IsNotNull(cache);

            string testSessionId = "Session1-" + testCacheId;
            ICacheSession session = await CreateSessionAsync(cache, testSessionId);

            // Use the testname to generate a CAS items.
            CasHash item = (await session.AddToCasAsync(TestName.AsStream())).Success();

            // Verify that we can read the content after it was added in
            // this session since it was pinned
            using (var stream = (await session.GetStreamAsync(item)).Success())
            {
                XAssert.AreEqual(TestName, stream.AsString(), "Failed to read back matching content from cache");
            }

            // We should have returned Ok since the content was not corrupted
            XAssert.AreEqual(ValidateContentStatus.Ok, (await session.ValidateContentAsync(item)).Success(), "Content should have matched in hash at this point!");

            // NoItem should always be valid
            XAssert.AreEqual(ValidateContentStatus.Ok, (await session.ValidateContentAsync(CasHash.NoItem)).Success(), "NoItem should always be valid!");

            // Corrupt the entry in the InMemory local cache
            await TestInMemory.CorruptEntry(cache.LocalCache, item);

            // Read it back and validate that it is corrupted
            using (var stream = (await session.GetStreamAsync(item)).Success())
            {
                XAssert.AreNotEqual(TestName, stream.AsString(), "Failed to corrupt CAS entry!");
            }

            ValidateContentStatus status = (await session.ValidateContentAsync(item)).Success();

            // At this point, caches can do a number of possible things
            // They can not return OK or NotImplemented (since we already checked that earlier)
            XAssert.AreNotEqual(ValidateContentStatus.Ok, status, "The item was corrupted - something should have happened");
            XAssert.AreNotEqual(ValidateContentStatus.NotSupported, status, "It was supported a moment earlier");

            await session.CloseAsync().SuccessAsync();
            await testCache.ShutdownAsync().SuccessAsync();
        }

        private void SetCorruptionBehavior(CallbackCacheReadOnlySessionWrapper session, CasHash item, ValidateContentStatus result, Action calledValidateContent)
        {
            session.PinToCasAsyncCallback = null;

            // Make the item we care about return bad data
            session.GetStreamAsyncCallback = (hash, hint, guid, realSession) =>
            {
                if (hash == item)
                {
                    // This is a stream of a different hash
                    return Task.FromResult<Possible<Stream, Failure>>("Corrupted!".AsStream());
                }

                return realSession.GetStreamAsync(hash, hint, guid);
            };

            session.ProduceFileAsyncCallback = (hash, filename, fileState, hint, guid, realSession) =>
            {
                if (hash == item)
                {
                    // This is a stream of a different hash
                    Directory.CreateDirectory(Path.GetDirectoryName(filename));
                    File.WriteAllText(filename, "Corrupted!");

                    return Task.FromResult<Possible<string, Failure>>(filename);
                }

                return realSession.ProduceFileAsync(hash, filename, fileState, hint, guid);
            };

            session.ValidateContentAsyncCallback = (hash, hint, guid, realSession) =>
            {
                if (hash == item)
                {
                    calledValidateContent();

                    switch (result)
                    {
                        case ValidateContentStatus.NotSupported:
                            // Nothing to do here as we act like a cache that does not
                            // have this feature
                            break;

                        case ValidateContentStatus.Invalid:
                            // Act like we identified the entry as bad but could
                            // not change that fact (which is basically leaving it as is
                            break;

                        case ValidateContentStatus.Ok:
                            // Set up the cache to be clean (item will work now)
                            session.GetStreamAsyncCallback = null;
                            session.ProduceFileAsyncCallback = null;
                            break;

                        case ValidateContentStatus.Remediated:
                            // Set up the cache to have "removed" the item
                            session.PinToCasAsyncCallback = (hash1, hint1, guid1, realSession1) =>
                            {
                                if (hash1 == item)
                                {
                                    return Task.FromResult<Possible<string, Failure>>(new NoCasEntryFailure(realSession1.CacheId, hash1));
                                }

                                return realSession1.PinToCasAsync(hash1, hint1, guid1);
                            };

                            session.GetStreamAsyncCallback = (hash1, hint1, guid1, realSession1) =>
                            {
                                if (hash1 == item)
                                {
                                    return Task.FromResult<Possible<Stream, Failure>>(new ProduceStreamFailure(realSession1.CacheId, hash1));
                                }

                                return realSession1.GetStreamAsync(hash1, hint1, guid1);
                            };

                            session.ProduceFileAsyncCallback = (hash1, filename1, fileState1, hint1, guid1, realSession1) =>
                            {
                                if (hash1 == item)
                                {
                                    return Task.FromResult<Possible<string, Failure>>(new ProduceFileFailure(realSession1.CacheId, hash1, filename1));
                                }

                                return realSession1.ProduceFileAsync(hash1, filename1, fileState1, hint1, guid1);
                            };
                            break;

                        default:
                            XAssert.Fail("We have a bug in the test itself");
                            break;
                    }

                    return Task.FromResult<Possible<ValidateContentStatus, Failure>>(result);
                }

                // For all other hashes, just pass through
                return realSession.ValidateContentAsync(hash, hint, guid);
            };
        }

        /// <nodoc/>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CorruptionRecoveryL2(bool useGetStream)
        {
            string testName = useGetStream ? "CorruptionRecoveryL2GetStream" : "CorruptionRecoveryL2ProduceFile";

            // In this case, we put a CAS entry into the L2 and corrupt it.  Then
            // we try to get it via the aggregator and have it call the ValidateContent
            // API on the L2 when it notices that the content is no good.
            string testCacheId = MakeCacheId(testName);
            ICache testCache = await InitializeCacheAsync(VerticalAggregatorDisconnectTests.NewWrappedRemoteCache(testCacheId, false, false)).SuccessAsync();
            VerticalAggregator.VerticalCacheAggregator cache = testCache as VerticalAggregator.VerticalCacheAggregator;
            XAssert.IsNotNull(cache);

            string testSessionId = "Session1-" + testCacheId;
            ICacheSession session = await CreateSessionAsync(cache, testSessionId);

            VerticalCacheAggregatorSession vSession = session as VerticalCacheAggregatorSession;
            XAssert.IsNotNull(vSession, "Where is our vertical aggregator session?");

            // Use the testname to generate a CAS item and put it directly in the remote session
            CasHash item = (await vSession.RemoteSession.AddToCasAsync(testName.AsStream())).Success();

            CallbackCacheSessionWrapper remoteSession = vSession.RemoteSession as CallbackCacheSessionWrapper;
            XAssert.IsNotNull(remoteSession, "Why did the remote session not become a callback session?");

            // Validate that pinning gets it from the L2 cache
            string cacheId = await session.PinToCasAsync(item).SuccessAsync();
            XAssert.AreEqual(remoteSession.CacheId, cacheId);

            int calledValidate = 0;
            Action countValidate = () => { calledValidate++; };

            // Test NotSupported
            calledValidate = 0;
            SetCorruptionBehavior(remoteSession, item, ValidateContentStatus.NotSupported, countValidate);
            if (useGetStream)
            {
                var possibleStream = await session.GetStreamAsync(item);
                XAssert.IsFalse(possibleStream.Succeeded, "Stream should have failed due to hash being invalid");
            }
            else
            {
                var possibleFile = await session.ProduceFileAsync(item, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("D"), "Test.txt"), FileState.Writeable);
                XAssert.IsFalse(possibleFile.Succeeded, "File should have failed due to hash being invalid");
            }

            XAssert.AreEqual(1, calledValidate, "Aggregator should have called ValidateContent exactly once");

            // Test Invalid
            calledValidate = 0;
            SetCorruptionBehavior(remoteSession, item, ValidateContentStatus.Invalid, countValidate);
            if (useGetStream)
            {
                var possibleStream = await session.GetStreamAsync(item);
                XAssert.IsFalse(possibleStream.Succeeded, "Stream should have failed due to hash being invalid");
            }
            else
            {
                var possibleFile = await session.ProduceFileAsync(item, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("D"), "Test.txt"), FileState.Writeable);
                XAssert.IsFalse(possibleFile.Succeeded, "File should have failed due to hash being invalid");
            }

            XAssert.AreEqual(1, calledValidate, "Aggregator should have called ValidateContent exactly once");

            // Test Remediated
            calledValidate = 0;
            SetCorruptionBehavior(remoteSession, item, ValidateContentStatus.Remediated, countValidate);
            if (useGetStream)
            {
                var possibleStream = await session.GetStreamAsync(item);
                XAssert.IsFalse(possibleStream.Succeeded, "Stream should have failed due to hash being invalid");
            }
            else
            {
                var possibleFile = await session.ProduceFileAsync(item, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("D"), "Test.txt"), FileState.Writeable);
                XAssert.IsFalse(possibleFile.Succeeded, "File should have failed due to hash being invalid");
            }

            XAssert.AreEqual(1, calledValidate, "Aggregator should have called ValidateContent exactly once");

            // This test has to be done last as it then populates into the local cache
            // Test Ok
            calledValidate = 0;
            SetCorruptionBehavior(remoteSession, item, ValidateContentStatus.Ok, countValidate);
            if (useGetStream)
            {
                (await session.GetStreamAsync(item).SuccessAsync()).Close();
            }
            else
            {
                string filename = await session.ProduceFileAsync(item, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("D"), "Test.txt"), FileState.Writeable).SuccessAsync();
                File.Delete(filename);
            }

            XAssert.AreEqual(1, calledValidate, "Aggregator should have called ValidateContent exactly once");

            // Verify that it now lives in the local cache
            cacheId = await session.PinToCasAsync(item).SuccessAsync();
            XAssert.AreEqual(cache.LocalCache.CacheId, cacheId);

            await session.CloseAsync().SuccessAsync();
            await testCache.ShutdownAsync().SuccessAsync();
        }

        /// <nodoc/>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [SuppressMessage("AsyncUsage", "AsyncFixer02", Justification = "ReadAllText and WriteAllText have async versions in .NET Standard which cannot be used in full framework.")]
        public async Task CorruptionRecovery3Layer(bool useGetStream)
        {
            string testName = useGetStream ? "CorruptionRecovery3LayerGetStream" : "CorruptionRecovery3LayerProduceFile";
            string testCacheId = MakeCacheId(testName);

            // 3 layer cache.
            TestInMemory memoryCache = new TestInMemory();

            string configL1 = memoryCache.NewCache(testCacheId + "L1", false);
            string configL2 = memoryCache.NewCache(testCacheId + "L2", false);
            string configL3 = memoryCache.NewCache(testCacheId + "L3", true, authoritative: true);

            string configL2ToL3 = NewCacheString(testCacheId + "V2", configL2, configL3, false, false, false);

            string vertConfig = NewCacheString(testCacheId, configL1, configL2ToL3, false, false, false);

            ICache testCache = await InitializeCacheAsync(vertConfig).SuccessAsync();

            string testSessionId = "Session1-" + testCacheId;
            ICacheSession session = await CreateSessionAsync(testCache, testSessionId);

            VerticalCacheAggregatorSession vSession = session as VerticalCacheAggregatorSession;
            XAssert.IsNotNull(vSession, "Where is our vertical aggregator session?");

            VerticalCacheAggregatorSession vSession2 = vSession.RemoteRoSession as VerticalCacheAggregatorSession;
            XAssert.IsNotNull(vSession2, "Where is our second vertical aggregator session?");

            var item = await vSession2.RemoteSession.AddToCasAsync(testName.AsStream()).SuccessAsync();

            // Check that Pin shows that it is in the L3
            XAssert.AreEqual(vSession2.RemoteSession.CacheId, await session.PinToCasAsync(item).SuccessAsync());

            var item2 = await vSession2.LocalSession.AddToCasAsync(testName.AsStream()).SuccessAsync();
            XAssert.AreEqual(item, item2);

            // Now, corrupt the item in vSession2.LocalSession
            await TestInMemory.CorruptEntry(((testCache as VerticalAggregator.VerticalCacheAggregator).RemoteCache as VerticalAggregator.VerticalCacheAggregator).LocalCache, item);

            // Now that we have the item in both L2 and L3, we should check what pin does
            // correctly identify it in the L2 and not in the L1 or L3
            XAssert.AreEqual(vSession2.LocalSession.CacheId, await session.PinToCasAsync(item).SuccessAsync());

            // Now pull that content via the full aggregator.  The L2 will produce corrupted
            // content but the aggregator will notice, do the ValidateContent call and then
            // try again, at which point the L3 will provide the correct content, fixing the
            // L2, at which point the L2 will provide the correct content to the L1
            if (useGetStream)
            {
                using (var stream = (await session.GetStreamAsync(item)).Success())
                {
                    XAssert.AreEqual(testName, stream.AsString(), "Failed to read back matching content from cache");
                }
            }
            else
            {
                var filename = await session.ProduceFileAsync(item, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("D"), "Test.txt"), FileState.Writeable).SuccessAsync();
                XAssert.AreEqual(testName, File.ReadAllText(filename));
            }

            // Verify by doing a pin and seeing that it comes in as being in the L1
            XAssert.AreEqual(vSession.LocalSession.CacheId, await session.PinToCasAsync(item).SuccessAsync());

            await session.CloseAsync().SuccessAsync();
            await testCache.ShutdownAsync().SuccessAsync();
        }

        /// <nodoc/>
        [Fact]
        public async Task PinBulkWithEmptyRequest()
        {
            string testName = "PinBulkWithEmptyRequest";
            string testCacheId = MakeCacheId(testName);

            // 3 layer cache.
            TestInMemory memoryCache = new TestInMemory();

            string configL1 = memoryCache.NewCache(testCacheId + "L1", false);
            string configL2 = memoryCache.NewCache(testCacheId + "L2", false);
            string configL3 = memoryCache.NewCache(testCacheId + "L3", true, authoritative: true);

            string configL2ToL3 = NewCacheString(testCacheId + "V2", configL2, configL3, false, false, false);

            string vertConfig = NewCacheString(testCacheId, configL1, configL2ToL3, false, false, false);

            ICache testCache = await CacheFactory.InitializeCacheAsync(vertConfig).SuccessAsync();

            string testSessionId = "Session1-" + testCacheId;
            ICacheSession session = await CreateSessionAsync(testCache, testSessionId);

            // Don't crash
            await session.PinToCasAsync(new CasEntries(new CasHash[0]));
        }

#if !FEATURE_SAFE_PROCESS_HANDLE
        [Fact]
        public async Task BadStreamDoesntCauseException()
        {
            string testName = "BadStreamDoesntCauseException";
            string testCacheId = MakeCacheId(testName);

            // 2 layer cache.
            TestInMemory memoryCache = new TestInMemory();

            // Use the WriteThrough option to force AddToCas to push the content to the remote.
            string cacheConfig = VerticalAggregatorDisconnectTests.NewWrappedLocalCache(testCacheId + "L1", false, true);

            ICache testCache = await CacheFactory.InitializeCacheAsync(cacheConfig).SuccessAsync();
            ICacheSession session = await testCache.CreateSessionAsync().SuccessAsync();

            VerticalCacheAggregatorSession vertSession = (VerticalCacheAggregatorSession)session;
            CallbackCacheSessionWrapper sessionWrapper = (CallbackCacheSessionWrapper)vertSession.LocalSession;

            sessionWrapper.GetStreamAsyncCallback = async (casHash, urgencyHint, activityId, realSession) =>
            {
                Stream realStream = await realSession.GetStreamAsync(casHash, urgencyHint, activityId).SuccessAsync();

                BadStreamWrapper badStream = new BadStreamWrapper(realStream);

                return badStream;
            };

            // Has to be a non-empty stream or the empty file short circuit blocks it.
            MemoryStream stream = new MemoryStream();
            stream.Capacity = 100;
            stream.WriteByte(Convert.ToByte('c'));

            await session.AddToCasAsync(stream).SuccessAsync();

            await session.CloseAsync().SuccessAsync();
            await testCache.ShutdownAsync().SuccessAsync();
        }
#endif
        private class BadStreamWrapper : MemoryStream
        {
            internal BadStreamWrapper(Stream s)
            {
                s.CopyTo(this);
            }

            public override long Length
            {
                get
                {
                    throw new Exception();
                }
            }
        }
    }
}
