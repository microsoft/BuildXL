// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Results;
using Xunit;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using Xunit.Abstractions;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions
{
    public abstract class CacheTests : TestWithOutput, IDisposable
    {
        protected const string Name = "name";
        protected const int DeterminismNone = 0;
        protected const int DeterminismCache1 = 1;
        protected const int DeterminismCache1Expired = 2;
        protected const int DeterminismCache2 = 3;
        protected const int DeterminismCache2Expired = 4;
        protected const int DeterminismTool = 5;
        protected const int DeterminismSinglePhaseNon = 6;

        protected static readonly CacheDeterminism[] Determinism =
        {
            CacheDeterminism.None,
            CacheDeterminism.ViaCache(new Guid("2ABD62E7-6117-4D8B-ABB2-D3346E5AF102"), DateTime.UtcNow + TimeSpan.FromDays(7)),
            CacheDeterminism.ViaCache(new Guid("2ABD62E7-6117-4D8B-ABB2-D3346E5AF102"), DateTime.UtcNow - TimeSpan.FromDays(7)),
            CacheDeterminism.ViaCache(new Guid("78559E55-E0C3-4C77-A908-8AE9E6590764"), DateTime.UtcNow + TimeSpan.FromDays(7)),
            CacheDeterminism.ViaCache(new Guid("78559E55-E0C3-4C77-A908-8AE9E6590764"), DateTime.UtcNow - TimeSpan.FromDays(7)),
            CacheDeterminism.Tool,
            CacheDeterminism.SinglePhaseNonDeterministic
        };

        protected static readonly CancellationToken Token = CancellationToken.None;
        protected readonly IAbsFileSystem FileSystem;
        protected readonly ILogger Logger;
        private const ImplicitPin ImplicitPinPolicy = ImplicitPin.None;
        private const int ContentByteCount = 100;
        private bool _disposed;

        /// <summary>
        ///     Level of deterministic authority guaranteed by the cache.
        /// </summary>
        protected enum AuthorityLevel
        {
            /// <summary>
            ///     The implementation is non-authoritative, instead accepting
            ///     and round-tripping authoritative determinism values given.
            /// </summary>
            None = 0,

            /// <summary>
            ///     The implementation reserves the right to provide a guarantee for
            ///     any return value, and will not accept external authorities over its own,
            ///     but it is not guaranteed to provide a guarantee for any particular call.
            /// </summary>
            Potential,

            /// <summary>
            ///     The implementation will immediately resolve/return a deterministic
            ///     authority value at the time of an AddOrGet.
            /// </summary>
            Immediate
        }

        protected CacheTests(Func<IAbsFileSystem> createFileSystemFunc, ILogger logger, ITestOutputHelper output = null)
            : base(output)
        {
            FileSystem = createFileSystemFunc();
            Logger = logger;
        }

        public override void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            base.Dispose();

            Dispose(true);
            GC.SuppressFinalize(this);

            _disposed = true;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                FileSystem?.Dispose();
                Logger?.Flush();
            }
        }

        /// <summary>
        ///     Gets a value indicating whether the subclass respects determinism values on ContentHashLists.
        /// </summary>
        /// <remarks>
        ///     The VSTS BuildCache currently does not care about these values value is a temporary feature to support BuildXL, and we don't
        ///     want to have to bake it into every implementation (including the VSTS BuildCache stack).
        ///     This allows implementations to opt out.
        /// </remarks>
        protected virtual bool RespectsToolDeterminism => true;

        /// <summary>
        ///     Gets a value indicating whether the subclass supports more than one hash type.
        /// </summary>
        protected virtual bool SupportsMultipleHashTypes => true;

        /// <summary>
        ///     Gets a value indicating the hash type preferred by the subclass.
        /// </summary>
        protected virtual HashType PreferredHashType => ChunkDedupEnabled ? HashType.DedupNodeOrChunk : HashType.Vso0;

        /// <summary>
        ///     Gets a value indicating whether or not the subclass implements EnumerateStrongFingerprints.
        /// </summary>
        protected virtual bool ImplementsEnumerateStrongFingerprints => true;

        /// <summary>
        ///     Gets a value indicating the type of authority offered by the implementation.
        /// </summary>
        protected virtual AuthorityLevel Authority => AuthorityLevel.None;

        /// <summary>
        ///     Gets a value indicating whether the cache uses Blob or Dedup Store.
        /// </summary>
        protected virtual bool ChunkDedupEnabled => false;

        /// <summary>
        ///     Gets a value indicating whether content for a new ContentHashList is required to exist before an AddOrGet.
        ///     Generally only authoritative caches should require this.
        /// </summary>
        protected bool RequiresContentExistenceOnAddOrGetContentHashList => Authority != AuthorityLevel.None;

        protected abstract ICache CreateCache(DisposableDirectory testDirectory);

        [Fact]
        public Task NameNotEmpty()
        {
            var context = new Context(Logger);
            return RunReadOnlySessionTestAsync(
                context,
                session =>
                {
                    Assert.False(string.IsNullOrEmpty(session.Name));
                    return Task.FromResult(0);
                });
        }

        [Fact]
        public Task StartupShutdown()
        {
            var context = new Context(Logger);
            return RunTestAsync(context, (ICache cache) => Task.FromResult(0));
        }

        [Fact]
        public Task CreateReadOnlySession()
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async cache =>
            {
                var result = cache.CreateReadOnlySession(context, Name, ImplicitPin.None).ShouldBeSuccess();

                using (IReadOnlyCacheSession session = result.Session)
                {
                    await session.StartupAsync(context).ShouldBeSuccess();
                    await session.ShutdownAsync(context).ShouldBeSuccess();
                }
            });
        }

        [Fact]
        public Task CreateSession()
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async cache =>
            {
                var result = cache.CreateSession(context, Name, ImplicitPin.None).ShouldBeSuccess();

                using (ICacheSession session = result.Session)
                {
                    await session.StartupAsync(context).ShouldBeSuccess();
                    await session.ShutdownAsync(context).ShouldBeSuccess();
                }
            });
        }

        [Fact]
        public async Task IdIsUnique()
        {
            var context = new Context(Logger);
            Guid cacheGuid1 = default(Guid);
            await RunTestAsync(context, cache =>
            {
                cacheGuid1 = cache.Id;
                Assert.NotEqual(CacheDeterminism.None.EffectiveGuid, cacheGuid1);
                Assert.NotEqual(CacheDeterminism.Tool.EffectiveGuid, cacheGuid1);
                return Task.FromResult(0);
            });
            Assert.NotEqual(default(Guid), cacheGuid1);
            await RunTestAsync(context, cache =>
            {
                Assert.NotEqual(cacheGuid1, cache.Id);
                Assert.NotEqual(CacheDeterminism.None.EffectiveGuid, cache.Id);
                Assert.NotEqual(CacheDeterminism.Tool.EffectiveGuid, cache.Id);
                return Task.FromResult(0);
            });
        }

        [Fact]
        public Task GetSelectorsGivesZeroTasks()
        {
            var context = new Context(Logger);
            var weakFingerprint = Fingerprint.Random();

            return RunReadOnlySessionTestAsync(context, async session =>
            {
                IEnumerable<GetSelectorResult> getSelectorResults = await session.GetSelectors(context, weakFingerprint, Token).ToList(Token);
                Assert.Equal(0, getSelectorResults.Count());
            });
        }

        [Fact]
        public Task GetSelectorsGivesSelectors()
        {
            var context = new Context(Logger);
            var weakFingerprint = Fingerprint.Random();

            return RunTestAsync(context, async session =>
            {
                var strongFingerprint1 = await CreateRandomStrongFingerprintAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session, weakFingerprint);
                var strongFingerprint2 = await CreateRandomStrongFingerprintAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session, weakFingerprint);

                var contentHashListWithDeterminism1 = await CreateRandomContentHashListWithDeterminismAsync(
                        context, RequiresContentExistenceOnAddOrGetContentHashList, session);
                var contentHashListWithDeterminism2 = await CreateRandomContentHashListWithDeterminismAsync(
                        context, RequiresContentExistenceOnAddOrGetContentHashList, session);

                await session.AddOrGetContentHashListAsync(context, strongFingerprint1, contentHashListWithDeterminism1, Token).ShouldBeSuccess();
                await session.AddOrGetContentHashListAsync(context, strongFingerprint2, contentHashListWithDeterminism2, Token).ShouldBeSuccess();

                var selector1 = strongFingerprint1.Selector;
                var selector2 = strongFingerprint2.Selector;

                List<GetSelectorResult> getSelectorResults = await session.GetSelectors(context, weakFingerprint, Token).ToList(Token);
                Assert.Equal(2, getSelectorResults.Count);

                GetSelectorResult getResult1 = getSelectorResults[0];
                Assert.True(getResult1.Succeeded);
                Assert.True(getResult1.Selector == selector1 || getResult1.Selector == selector2);

                GetSelectorResult getResult2 = getSelectorResults[1];
                Assert.True(getResult1.Succeeded);
                Assert.True(getResult2.Selector == selector1 || getResult2.Selector == selector2);
            });
        }

        [Fact]
        public Task GetNonExisting()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random(selectorHashType: PreferredHashType);

            return RunReadOnlySessionTestAsync(context, async session =>
            {
                var result = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                Assert.Equal(new GetContentHashListResult(new ContentHashListWithDeterminism(null, CacheDeterminism.None)), result);
            });
        }

        [Fact]
        public Task GetExisting()
        {
            var context = new Context(Logger);

            return RunTestAsync(context, async session =>
            {
                var strongFingerprint = await CreateRandomStrongFingerprintAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session);
                var contentHashListWithDeterminism = await CreateRandomContentHashListWithDeterminismAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session);
                await session.AddOrGetContentHashListAsync(context, strongFingerprint, contentHashListWithDeterminism, Token).ShouldBeSuccess();

                var result = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                Assert.Equal(contentHashListWithDeterminism.ContentHashList, result.ContentHashListWithDeterminism.ContentHashList);
            });
        }

        /// <summary>
        ///     Authoritative caches generally require that content be added before ContentHashLists which refer
        ///     to that content. Otherwise the ContentHashList would be unbacked and subject to change as soon
        ///     as another caller tried to add a new ContentHashList, and the authoritative cache would not be
        ///     able to drive convergence until a backed ContentHashList won the race.
        /// </summary>
        [Fact]
        public Task AuthoritativeCachesGiveErrorOnAddOrGetBeforeContentIsAdded()
        {
            if (!RequiresContentExistenceOnAddOrGetContentHashList)
            {
                return Task.FromResult(0);
            }

            var context = new Context(Logger);

            return RunTestAsync(context, async session =>
            {
                var strongFingerprint = await CreateRandomStrongFingerprintAsync(context, false, session);
                var contentHashListWithDeterminism =
                    await CreateRandomContentHashListWithDeterminismAsync(context, false, session);
                var addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token);

                Assert.False(addResult.Succeeded);
            });
        }

        /// <summary>
        ///     This ensures that the requirement holds even when a backed value already exists.
        /// </summary>
        [Fact]
        public Task AuthoritativeCachesGiveErrorOnAddOrGetBeforeContentIsAddedEvenWhenDifferentValueExists()
        {
            if (!RequiresContentExistenceOnAddOrGetContentHashList)
            {
                return Task.FromResult(0);
            }

            var context = new Context(Logger);

            return RunTestAsync(context, async session =>
            {
                var strongFingerprint = await CreateRandomStrongFingerprintAsync(context, true, session);
                var contentHashListWithDeterminism1 =
                    await CreateRandomContentHashListWithDeterminismAsync(context, true, session);
                var addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism1, Token);
                Assert.True(addResult.Succeeded);

                var contentHashListWithDeterminism2 =
                    await CreateRandomContentHashListWithDeterminismAsync(context, false, session);
                addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism2, Token);
                Assert.False(addResult.Succeeded);
            });
        }

        [Fact]
        public Task AddOrGetAddsNew()
        {
            var context = new Context(Logger);

            return RunTestAsync(context, async session =>
            {
                var strongFingerprint = await CreateRandomStrongFingerprintAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session);
                var contentHashListWithDeterminism = await CreateRandomContentHashListWithDeterminismAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session);

                var addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token);
                Assert.Equal(null, addResult.ContentHashListWithDeterminism.ContentHashList);

                var getResult = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                Assert.Equal(contentHashListWithDeterminism.ContentHashList, getResult.ContentHashListWithDeterminism.ContentHashList);
            });
        }

        [Fact]
        public Task AddOrGetGetsExisting()
        {
            var context = new Context(Logger);

            return RunTestAsync(context, async session =>
            {
                var strongFingerprint = await CreateRandomStrongFingerprintAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session);
                var contentHashListWithDeterminism = await CreateRandomContentHashListWithDeterminismAsync(
                    context, true, session);

                var addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token);
                Assert.Equal(null, addResult.ContentHashListWithDeterminism.ContentHashList);

                var newContentHashListWithDeterminism = await CreateRandomContentHashListWithDeterminismAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session);
                addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, newContentHashListWithDeterminism, Token);
                Assert.Equal(contentHashListWithDeterminism.ContentHashList, addResult.ContentHashListWithDeterminism.ContentHashList);
            });
        }

        [Fact]
        public Task AddOrGetEquivalentIsAccepted()
        {
            var context = new Context(Logger);

            return RunTestAsync(context, async session =>
            {
                var strongFingerprint = await CreateRandomStrongFingerprintAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session);
                var contentHashListWithDeterminism = await CreateRandomContentHashListWithDeterminismAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session);

                var addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token);
                Assert.Equal(null, addResult.ContentHashListWithDeterminism.ContentHashList);

                addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token);
                Assert.Equal(null, addResult.ContentHashListWithDeterminism.ContentHashList);
            });
        }

        [Fact]
        public Task AddMultipleHashTypes()
        {
            if (!SupportsMultipleHashTypes)
            {
                return Task.FromResult(0);
            }

            var context = new Context(Logger);

            return RunTestAsync(context, async session =>
            {
                StrongFingerprint strongFingerprint;
                ContentHashList contentHashList;
                if (RequiresContentExistenceOnAddOrGetContentHashList)
                {
                    var putResult = await session.PutRandomAsync(context, PreferredHashType, false, ContentByteCount, Token);
                    var content1 = putResult.ContentHash;
                    putResult = await session.PutRandomAsync(context, HashType.SHA1, false, ContentByteCount, Token);
                    var content2 = putResult.ContentHash;
                    putResult = await session.PutRandomAsync(context, HashType.MD5, false, ContentByteCount, Token);
                    var content3 = putResult.ContentHash;
                    putResult = await session.PutRandomAsync(context, PreferredHashType, false, ContentByteCount, Token);
                    var selectorContentHash = putResult.ContentHash;

                    strongFingerprint = new StrongFingerprint(
                        Fingerprint.Random(), new Selector(selectorContentHash, Selector.Random().Output));
                    contentHashList = new ContentHashList(new[]
                    {
                        content1,
                        content2,
                        content3
                    });
                }
                else
                {
                    strongFingerprint = StrongFingerprint.Random(selectorHashType: PreferredHashType);
                    contentHashList = new ContentHashList(new[]
                    {
                        ContentHash.Random(PreferredHashType),
                        ContentHash.Random(HashType.SHA1),
                        ContentHash.Random(HashType.MD5)
                    });
                }

                var contentHashListWithDeterminism = new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None);
                await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token).ShouldBeSuccess();

                var result = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                Assert.Equal(contentHashListWithDeterminism.ContentHashList, result.ContentHashListWithDeterminism.ContentHashList);
            });
        }

        [Fact]
        public Task AddPayload()
        {
            var context = new Context(Logger);
            var payload = new byte[] { 0, 1, 2, 3 };

            return RunTestAsync(context, async session =>
            {
                var strongFingerprint = await CreateRandomStrongFingerprintAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session);
                var contentHashListWithDeterminism = await CreateRandomContentHashListWithDeterminismAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session, payload);
                await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token).ShouldBeSuccess();

                var result = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                Assert.Equal(contentHashListWithDeterminism.ContentHashList, result.ContentHashListWithDeterminism.ContentHashList);
                Assert.True(result.ContentHashListWithDeterminism.ContentHashList.Payload.SequenceEqual(payload));
            });
        }

        [Fact]
        public Task AddNullPayload()
        {
            var context = new Context(Logger);

            return RunTestAsync(context, async session =>
            {
                var strongFingerprint = await CreateRandomStrongFingerprintAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session);

                // ReSharper disable once RedundantArgumentDefaultValue
                var contentHashListWithDeterminism = await CreateRandomContentHashListWithDeterminismAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session);
                var addresult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token);

                var result = await session.GetContentHashListAsync(context, strongFingerprint, Token);

                Assert.Equal(contentHashListWithDeterminism.ContentHashList, result.ContentHashListWithDeterminism.ContentHashList);
                Assert.Null(result.ContentHashListWithDeterminism.ContentHashList.Payload);
            });
        }

        [Fact]
        public Task ToolDeterminismIsRoundTripped()
        {
            var context = new Context(Logger);

            return RunTestAsync(context, async session =>
            {
                var strongFingerprint = await CreateRandomStrongFingerprintAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session);
                var contentHashListWithDeterminism = await CreateRandomContentHashListWithDeterminismAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session, determinism: CacheDeterminism.Tool);
                var addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token);
                Assert.True(addResult.Succeeded);
                Assert.Null(addResult.ContentHashListWithDeterminism.ContentHashList);
                Assert.Equal(CacheDeterminism.Tool, addResult.ContentHashListWithDeterminism.Determinism);

                var getResult = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                Assert.True(getResult.Succeeded);
                Assert.Equal(contentHashListWithDeterminism.ContentHashList, getResult.ContentHashListWithDeterminism.ContentHashList);
                Assert.Equal(contentHashListWithDeterminism.Determinism, getResult.ContentHashListWithDeterminism.Determinism);
            });
        }

        [Theory]
        [InlineData(DeterminismNone, DeterminismSinglePhaseNon)] // Overwriting SinglePhaseNonDeterministic with anything else is an error.
        [InlineData(DeterminismCache1, DeterminismSinglePhaseNon)]
        [InlineData(DeterminismCache1Expired, DeterminismSinglePhaseNon)]
        [InlineData(DeterminismTool, DeterminismSinglePhaseNon)]
        [InlineData(DeterminismSinglePhaseNon, DeterminismNone)] // Overwriting anything else with SinglePhaseNonDeterministic is an error.
        [InlineData(DeterminismSinglePhaseNon, DeterminismCache1)]
        [InlineData(DeterminismSinglePhaseNon, DeterminismCache1Expired)]
        [InlineData(DeterminismSinglePhaseNon, DeterminismTool)]
        public Task MismatchedSinglePhaseFails(int fromDeterminism, int toDeterminism)
        {
            if (Authority != AuthorityLevel.None)
            {
                return Task.FromResult(0);
            }

            var context = new Context(Logger);

            return RunTestAsync(context, async session =>
            {
                var strongFingerprint = await CreateRandomStrongFingerprintAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session);
                var contentHashListWithDeterminism = await CreateRandomContentHashListWithDeterminismAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session, determinism: Determinism[fromDeterminism]);
                var addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token);
                Assert.Equal(Determinism[fromDeterminism].EffectiveGuid, addResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);

                // What we will do here is AddOrGet() a record that we already know is
                // there but with the determinism bit changed.
                addResult = await session.AddOrGetContentHashListAsync(
                    context,
                    strongFingerprint,
                    new ContentHashListWithDeterminism(contentHashListWithDeterminism.ContentHashList, Determinism[toDeterminism]),
                    Token);
                Assert.Equal(AddOrGetContentHashListResult.ResultCode.SinglePhaseMixingError, addResult.Code);
            });
        }

        [Theory]
        [InlineData(DeterminismNone, DeterminismTool, true)] // Tool overwrites None
        [InlineData(DeterminismNone, DeterminismCache1, true)] // ViaCache overwrites None
        [InlineData(DeterminismCache1, DeterminismCache2, true)] // ViaCache overwrites other ViaCache...
        [InlineData(DeterminismCache2, DeterminismCache1, true)] // ...in either direction
        [InlineData(DeterminismCache1, DeterminismTool, true)] // Tool overwrites ViaCache
        [InlineData(DeterminismTool, DeterminismNone, false)] // None does not overwrite Tool
        [InlineData(DeterminismTool, DeterminismCache1, false)] // ViaCache does not overwrite Tool
        [InlineData(DeterminismCache1, DeterminismNone, false)] // None does not overwrite ViaCache
        [InlineData(DeterminismNone, DeterminismNone, false)] // None does not overwrite None
        [InlineData(DeterminismCache1, DeterminismCache1, false)] // ViaCache does not overwrite same ViaCache
        [InlineData(DeterminismTool, DeterminismTool, false)] // Tool does not overwrite Tool
        [InlineData(DeterminismSinglePhaseNon, DeterminismSinglePhaseNon, true)] // SinglePhaseNonDeterministic overwrites itself
        [InlineData(DeterminismCache1Expired, DeterminismTool, true)] // Expired behaves like None in all cases
        [InlineData(DeterminismCache1Expired, DeterminismCache1, true)]
        [InlineData(DeterminismTool, DeterminismCache1Expired, false)]
        [InlineData(DeterminismCache1, DeterminismCache1Expired, false)]
        [InlineData(DeterminismCache1Expired, DeterminismCache1Expired, false)]
        [InlineData(DeterminismCache1Expired, DeterminismCache2Expired, false)]
        public Task DeterminismUpgradeWhenPreviousContentExists(int fromDeterminism, int toDeterminism, bool shouldUpgrade)
        {
            if (Authority != AuthorityLevel.None)
            {
                return Task.FromResult(0);
            }

            var context = new Context(Logger);

            return RunTestAsync(context, async session =>
            {
                var strongFingerprint = await CreateRandomStrongFingerprintAsync(
                    context, true, session);
                var contentHashListWithDeterminism = await CreateRandomContentHashListWithDeterminismAsync(
                    context, true, session, determinism: Determinism[fromDeterminism]);
                var addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token);
                Assert.Equal(Determinism[fromDeterminism].EffectiveGuid, addResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);

                // What we will do here is AddOrGet() a record that we already know is
                // there but with the determinism bit changed.
                addResult = await session.AddOrGetContentHashListAsync(
                    context,
                    strongFingerprint,
                    new ContentHashListWithDeterminism(contentHashListWithDeterminism.ContentHashList, Determinism[toDeterminism]),
                    Token);
                Assert.Null(addResult.ContentHashListWithDeterminism.ContentHashList);
                Assert.Equal(
                    Determinism[shouldUpgrade ? toDeterminism : fromDeterminism].EffectiveGuid,
                    addResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);

                var getResult = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                Assert.Equal(
                    Determinism[shouldUpgrade ? toDeterminism : fromDeterminism].EffectiveGuid,
                    getResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);
            });
        }

        [Theory]
        [InlineData(DeterminismNone)]
        [InlineData(DeterminismCache1)]
        [InlineData(DeterminismCache2)]
        [InlineData(DeterminismCache1Expired)]
        [InlineData(DeterminismCache2Expired)]
        public Task ImmediatelyAuthoritativeCachesIgnoreNonToolDeterminismAndReturnCacheId(int determinism)
        {
            if (Authority != AuthorityLevel.Immediate)
            {
                return Task.FromResult(0);
            }

            var context = new Context(Logger);

            return RunTestAsync(context, async (cache, session) =>
            {
                var strongFingerprint = await CreateRandomStrongFingerprintAsync(
                    context, true, session);
                var contentHashListWithDeterminism = await CreateRandomContentHashListWithDeterminismAsync(
                    context, true, session, determinism: Determinism[determinism]);

                // Add new
                var addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token);
                Assert.Equal(cache.Id, addResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);

                // Add existing
                addResult = await session.AddOrGetContentHashListAsync(
                    context,
                    strongFingerprint,
                    new ContentHashListWithDeterminism(contentHashListWithDeterminism.ContentHashList, Determinism[determinism]),
                    Token);
                Assert.Null(addResult.ContentHashListWithDeterminism.ContentHashList);
                Assert.Equal(cache.Id, addResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);

                // Get existing
                var getResult = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                Assert.Equal(cache.Id, getResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);
            });
        }

        [Fact]
        public Task ChangingToolDeterministicFailsWhenPreviousContentExists()
        {
            if (!RespectsToolDeterminism)
            {
                return Task.FromResult(0);
            }

            var context = new Context(Logger);

            return RunTestAsync(context, async session =>
            {
                var strongFingerprint = await CreateRandomStrongFingerprintAsync(context, true, session);
                var contentHashListWithDeterminism = await CreateRandomContentHashListWithDeterminismAsync(
                    context, true, session, determinism: CacheDeterminism.Tool);
                var addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token);
                Assert.Equal(CacheDeterminism.Tool.EffectiveGuid, addResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);

                // Add a new, different ContentHashList
                var newContentHashListWithDeterminism = await CreateRandomContentHashListWithDeterminismAsync(
                    context, RequiresContentExistenceOnAddOrGetContentHashList, session, determinism: CacheDeterminism.Tool);
                addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, newContentHashListWithDeterminism, Token);
                Assert.Equal(AddOrGetContentHashListResult.ResultCode.InvalidToolDeterminismError, addResult.Code);
                Assert.Equal(contentHashListWithDeterminism.ContentHashList, addResult.ContentHashListWithDeterminism.ContentHashList);
            });
        }

        [Fact]
        public Task PinNonExisting()
        {
            var context = new Context(Logger);
            return RunReadOnlySessionTestAsync(context, async session =>
            {
                var result = await session.PinAsync(context, ContentHash.Random(PreferredHashType), Token);
                Assert.Equal(PinResult.ResultCode.ContentNotFound, result.Code);
            });
        }

        [Fact]
        public Task PinExisting()
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async session =>
            {
                var putResult = await session.PutRandomAsync(context, PreferredHashType, false, ContentByteCount, Token);
                await session.PinAsync(context, putResult.ContentHash, Token).ShouldBeSuccess();
            });
        }

        [Fact]
        public Task BulkPinNonExisting()
        {
            var context = new Context(Logger);
            return RunReadOnlySessionTestAsync(context, async session =>
            {
                var fileCount = 5;
                var randomHashes = Enumerable.Range(0, fileCount).Select(i => ContentHash.Random()).ToList();
                var results = (await session.PinAsync(context, randomHashes, Token)).ToList();
                Assert.Equal(fileCount, results.Count);
                foreach (var result in results)
                {
                    var pinResult = await result;
                    Assert.Equal(PinResult.ResultCode.ContentNotFound, pinResult.Item.Code);
                }
            });
        }

        [Fact]
        public Task BulkPinSomeExisting()
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async session =>
            {
                var fileCount = 3;
                var addedHashes = await session.PutRandomAsync(context, PreferredHashType, false, fileCount, ContentByteCount, true);
                var randomHashes = Enumerable.Range(0, fileCount).Select(i => ContentHash.Random());

                // First half are random missing hashes and remaining are actually present
                var hashesToQuery = randomHashes.Concat(addedHashes).ToList();

                var results = (await session.PinAsync(context, hashesToQuery, Token)).ToList();
                Assert.Equal(2 * fileCount, results.Count);
                foreach (var result in results)
                {
                    var pinResult = await result;
                    if (pinResult.Index < fileCount)
                    {
                        Assert.Equal(PinResult.ResultCode.ContentNotFound, pinResult.Item.Code);
                    }
                    else
                    {
                        pinResult.Item.ShouldBeSuccess();
                    }
                }
            });
        }

        [Fact]
        public Task BulkPinExisting()
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async session =>
            {
                var fileCount = 5;
                var contentHashes = await session.PutRandomAsync(context, PreferredHashType, false, fileCount, ContentByteCount, true);
                var results = (await session.PinAsync(context, contentHashes, Token)).ToList();
                Assert.Equal(fileCount, results.Count);
                foreach (var result in results)
                {
                    var pinResult = await result;
                    pinResult.Item.ShouldBeSuccess();
                }
            });
        }

        [Fact]
        public Task OpenStreamNonExisting()
        {
            var context = new Context(Logger);
            return RunReadOnlySessionTestAsync(context, async session =>
            {
                await session.OpenStreamAsync(context, ContentHash.Random(PreferredHashType), Token).ShouldBeNotFound();
            });
        }

        [Fact]
        public Task OpenStreamExisting()
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async session =>
            {
                var putResult = await session.PutRandomAsync(
                    context, PreferredHashType, false, ContentByteCount, Token);
                var result = await session.OpenStreamAsync(context, putResult.ContentHash, Token).ShouldBeSuccess();
                Assert.NotNull(result.Stream);
                Assert.Equal(ContentByteCount, result.Stream.Length);
                result.Stream.Dispose();
            });
        }

        [Fact]
        public Task OpenStreamExistingEmpty()
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async session =>
            {
                using (var stream = new MemoryStream(new byte[0]))
                {
                    var r1 = await session.PutStreamAsync(context, PreferredHashType, stream, Token);
                    var r2 = await session.OpenStreamAsync(context, r1.ContentHash, Token).ShouldBeSuccess();
                    Assert.NotNull(r2.Stream);
                    r2.Stream.Dispose();
                }
            });
        }

        [Fact]
        public Task PlaceFileNonExisting()
        {
            var context = new Context(Logger);
            return RunReadOnlySessionTestAsync(context, async session =>
            {
                var path = new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("Z", "nonexist", "file.dat"));
                var result = await session.PlaceFileAsync(
                    context,
                    ContentHash.Random(PreferredHashType),
                    path,
                    FileAccessMode.ReadOnly,
                    FileReplacementMode.ReplaceExisting,
                    FileRealizationMode.Any,
                    Token);
                Assert.Equal(PlaceFileResult.ResultCode.NotPlacedContentNotFound, result.Code);
            });
        }

        [Fact]
        public async Task PlaceFileExisting()
        {
            using (var placeDirectory = new DisposableDirectory(FileSystem))
            {
                var path = placeDirectory.Path / "file.dat";

                var context = new Context(Logger);
                await RunTestAsync(context, async session =>
                {
                    var putResult = await session.PutRandomAsync(
                        context, PreferredHashType, false, ContentByteCount, Token);
                    var result = await session.PlaceFileAsync(
                        context,
                        putResult.ContentHash,
                        path,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.ReplaceExisting,
                        FileRealizationMode.Any,
                        Token);
                    Assert.True(result.IsPlaced());
                    Assert.True(FileSystem.FileExists(path));
                });
            }
        }

        [Fact]
        public async Task PlaceFileExistingReplaces()
        {
            using (var placeDirectory = new DisposableDirectory(FileSystem))
            {
                var path = placeDirectory.Path / "file.dat";
                FileSystem.WriteAllBytes(path, new byte[0]);

                var context = new Context(Logger);
                await RunTestAsync(context, async session =>
                {
                    var putResult = await session.PutRandomAsync(
                        context, PreferredHashType, false, ContentByteCount, Token);
                    var result = await session.PlaceFileAsync(
                        context,
                        putResult.ContentHash,
                        path,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.ReplaceExisting,
                        FileRealizationMode.Any,
                        Token);
                    Assert.True(result.IsPlaced());
                    Assert.True(FileSystem.FileExists(path));
                });
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task PutNonexistentFile(bool provideHash)
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async session =>
            {
                var path = new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("Z", "nonexist", "file.dat"));
                PutResult result;
                if (provideHash)
                {
                    result = await session.PutFileAsync(
                        context, ContentHash.Random(PreferredHashType), path, FileRealizationMode.Any, Token);
                }
                else
                {
                    result = await session.PutFileAsync(
                        context, PreferredHashType, path, FileRealizationMode.Any, Token);
                }

                result.ShouldBeError();
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task PutFileNonExisting(bool provideHash)
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async session =>
            {
                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    var data = ThreadSafeRandom.GetBytes(ContentByteCount);
                    var hash = HashInfoLookup.Find(PreferredHashType).CreateContentHasher().GetContentHash(data);
                    var path = tempDirectory.CreateRandomFileName();
                    FileSystem.WriteAllBytes(path, data);

                    PutResult putResult = provideHash
                        ? await session.PutFileAsync(context, hash, path, FileRealizationMode.Any, Token)
                        : await session.PutFileAsync(context, PreferredHashType, path, FileRealizationMode.Any, Token);

                    putResult.ShouldBeSuccess();
                    Assert.Equal(hash, putResult.ContentHash);
                    Assert.Equal(data.Length, putResult.ContentSize);
                }
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task PutFileExisting(bool provideHash)
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async session =>
            {
                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    var data = ThreadSafeRandom.GetBytes(ContentByteCount);
                    var hash = HashInfoLookup.Find(PreferredHashType).CreateContentHasher().GetContentHash(data);
                    var path = tempDirectory.CreateRandomFileName();
                    FileSystem.WriteAllBytes(path, data);

                    PutResult putResult = await
                        session.PutFileAsync(context, hash, path, FileRealizationMode.Any, Token).ShouldBeSuccess();

                    putResult = provideHash
                        ? await session.PutFileAsync(context, hash, path, FileRealizationMode.Any, Token)
                        : await session.PutFileAsync(context, PreferredHashType, path, FileRealizationMode.Any, Token);

                    putResult.ShouldBeSuccess();
                    Assert.Equal(hash, putResult.ContentHash);
                    Assert.Equal(data.Length, putResult.ContentSize);
                }
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task PutStreamNonExisting(bool provideHash)
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async session =>
            {
                var data = ThreadSafeRandom.GetBytes(ContentByteCount);
                var hash = HashInfoLookup.Find(PreferredHashType).CreateContentHasher().GetContentHash(data);
                using (var stream = new MemoryStream(data))
                {
                    PutResult putResult = provideHash
                        ? await session.PutStreamAsync(context, hash, stream, Token)
                        : await session.PutStreamAsync(context, PreferredHashType, stream, Token);

                    putResult.ShouldBeSuccess();
                    Assert.Equal(hash, putResult.ContentHash);
                    Assert.Equal(data.Length, putResult.ContentSize);
                }
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task PutStreamExisting(bool provideHash)
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async session =>
            {
                var data = ThreadSafeRandom.GetBytes(ContentByteCount);
                var hash = HashInfoLookup.Find(PreferredHashType).CreateContentHasher().GetContentHash(data);
                using (var stream = new MemoryStream(data))
                {
                    await session.PutStreamAsync(context, hash, stream, Token).ShouldBeSuccess();
                }

                using (var stream = new MemoryStream(data))
                {
                    PutResult putResult = provideHash
                        ? await session.PutStreamAsync(context, hash, stream, Token)
                        : await session.PutStreamAsync(context, PreferredHashType, stream, Token);

                    putResult.ShouldBeSuccess();
                    Assert.Equal(hash, putResult.ContentHash);
                    Assert.Equal(data.Length, putResult.ContentSize);
                }
            });
        }

        [Fact]
        public Task EnumerateStrongFingerprintsEmpty()
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async cache =>
            {
                using (var strongFingerprintEnumerator = cache.EnumerateStrongFingerprints(context).GetEnumerator())
                {
                    Assert.Equal(false, await strongFingerprintEnumerator.MoveNext());
                }
            });
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1337)]
        public virtual Task EnumerateStrongFingerprints(int strongFingerprintCount)
        {
            if (!ImplementsEnumerateStrongFingerprints)
            {
                return Task.FromResult(0);
            }

            var context = new Context(Logger);
            return RunTestAsync(context, async (cache, session) =>
            {
                var expected = await AddRandomContentHashListsAsync(context, strongFingerprintCount, session);
                var enumerated =
                    (await cache.EnumerateStrongFingerprints(context).ToList())
                    .Where(result => result.Succeeded)
                    .Select(result => result.Data)
                    .ToHashSet();
                Assert.Equal(expected.Count, enumerated.Count);
                Assert.True(expected.SetEquals(enumerated));
            });
        }

        private async Task<HashSet<StrongFingerprint>> AddRandomContentHashListsAsync(Context context, int count, ICacheSession session)
        {
            var strongFingerprints = new HashSet<StrongFingerprint>();
            for (int i = 0; i < count; i++)
            {
                var strongFingerprint = StrongFingerprint.Random();
                var contentHashListWithDeterminism = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);
                await session.AddOrGetContentHashListAsync(context, strongFingerprint, contentHashListWithDeterminism, Token).ShouldBeSuccess();
                strongFingerprints.Add(strongFingerprint);
            }

            return strongFingerprints;
        }

        private Task RunReadOnlySessionTestAsync(Context context, Func<IReadOnlyCacheSession, Task> funcAsync)
        {
            return RunReadOnlySessionTestAsync(context, funcAsync, CreateCache);
        }

        protected Task RunReadOnlySessionTestAsync(
            Context context, Func<IReadOnlyCacheSession, Task> funcAsync, Func<DisposableDirectory, ICache> createCacheFunc)
        {
            return RunTestAsync(
                context,
                async cache =>
                {
                    var createSessionResult = cache.CreateReadOnlySession(context, Name, ImplicitPinPolicy).ShouldBeSuccess();
                    using (var session = createSessionResult.Session)
                    {
                        try
                        {
                            await session.StartupAsync(context).ShouldBeSuccess();
                            await funcAsync(session);
                        }
                        finally
                        {
                            await session.ShutdownAsync(context).ShouldBeSuccess();
                        }
                    }
                },
                createCacheFunc);
        }

        protected async Task RunTestAsync(Context context, ICache cache, Func<ICacheSession, Task> funcAsync)
        {
            var createSessionResult = cache.CreateSession(context, Name, ImplicitPinPolicy).ShouldBeSuccess();
            using (var session = createSessionResult.Session)
            {
                await session.StartupAsync(context).ShouldBeSuccess();
                await funcAsync(session);
                await session.ShutdownAsync(context).ShouldBeSuccess();
            }
        }

        private Task RunTestAsync(Context context, Func<ICache, ICacheSession, Task> funcAsync)
        {
            return RunTestAsync(context, (cache, session, testDirectory) => funcAsync(cache, session), CreateCache);
        }

        protected Task RunTestAsync(Context context, Func<ICacheSession, Task> funcAsync)
        {
            return RunTestAsync(context, funcAsync, CreateCache);
        }

        protected Task RunTestAsync(
            Context context, Func<ICacheSession, Task> funcAsync, Func<DisposableDirectory, ICache> createCacheFunc)
        {
            return RunTestAsync(context, (session, testDirectory) => funcAsync(session), createCacheFunc);
        }

        private Task RunTestAsync(
            Context context, Func<ICacheSession, DisposableDirectory, Task> funcAsync, Func<DisposableDirectory, ICache> createCacheFunc)
        {
            return RunTestAsync(context, (cache, session, testDirectory) => funcAsync(session, testDirectory), createCacheFunc);
        }

        protected async Task RunTestAsync(
            Context context,
            Func<ICache, ICacheSession, DisposableDirectory, Task> funcAsync,
            Func<DisposableDirectory, ICache> createCacheFunc)
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                await RunTestAsync(
                    context,
                    (cache, session) => funcAsync(cache, session, testDirectory),
                    () => createCacheFunc(testDirectory));
            }
        }

        protected Task RunTestAsync(
            Context context, Func<ICache, Task> funcAsync)
        {
            return RunTestAsync(context, funcAsync, CreateCache);
        }

        private async Task RunTestAsync(
            Context context, Func<ICache, Task> funcAsync, Func<DisposableDirectory, ICache> createCacheFunc)
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                await RunTestAsync(context, funcAsync, () => createCacheFunc(testDirectory));
            }
        }

        protected Task RunTestAsync(
            Context context,
            Func<ICache, ICacheSession, Task> funcAsync,
            Func<ICache> createCacheFunc)
        {
            // ReSharper disable once ConvertClosureToMethodGroup
            return RunTestAsync(
                context,
                cache => RunTestAsync(context, cache, session => funcAsync(cache, session)),
                () => createCacheFunc());
        }

        private static async Task RunTestAsync(
            Context context,
            Func<ICache, Task> funcAsync,
            Func<ICache> createCacheFunc)
        {
            using (var cache = createCacheFunc())
            {
                try
                {
                    await cache.StartupAsync(context).ShouldBeSuccess();
                    await funcAsync(cache);
                }
                finally
                {
                    await cache.ShutdownAsync(context).ShouldBeSuccess();
                }
            }
        }

        protected Task<StrongFingerprint> CreateRandomStrongFingerprintAsync(
            Context context, bool putContentFirst, ICacheSession session)
        {
            return CreateRandomStrongFingerprintAsync(context, putContentFirst, session, Fingerprint.Random());
        }

        protected Task<StrongFingerprint> CreateRandomStrongFingerprintAsync(
            Context context, bool putContentFirst, ICacheSession session, Fingerprint weakFingerprint)
        {
            return putContentFirst
                ? CreateBackedStrongFingerprintAsync(context, session, weakFingerprint)
                : Task.FromResult(new StrongFingerprint(weakFingerprint, Selector.Random(PreferredHashType)));
        }

        private async Task<StrongFingerprint> CreateBackedStrongFingerprintAsync(
            Context context, ICacheSession session, Fingerprint weakFingerprint)
        {
            var putResult = await session.PutRandomAsync(context, PreferredHashType, false, ContentByteCount, Token);
            Assert.True(putResult.Succeeded);
            return new StrongFingerprint(weakFingerprint, new Selector(putResult.ContentHash, Selector.Random().Output));
        }

        protected Task<ContentHashListWithDeterminism> CreateRandomContentHashListWithDeterminismAsync(
            Context context,
            bool putContentFirst,
            ICacheSession session,
            byte[] payload = null,
            CacheDeterminism determinism = default(CacheDeterminism))
        {
            return putContentFirst
                ? CreateBackedContentHashListWithDeterminismAsync(context, session, payload, determinism)
                : Task.FromResult(
                    new ContentHashListWithDeterminism(ContentHashList.Random(PreferredHashType, payload: payload), determinism));
        }

        private async Task<ContentHashListWithDeterminism> CreateBackedContentHashListWithDeterminismAsync(
            Context context, ICacheSession session, byte[] payload, CacheDeterminism determinism)
        {
            const bool useExactSize = false;
            const int fileCount = 2;
            var randomContent = await session.PutRandomAsync(
                context, PreferredHashType, false, fileCount, ContentByteCount, useExactSize);
            return new ContentHashListWithDeterminism(new ContentHashList(randomContent.ToArray(), payload), determinism);
        }
    }
}
