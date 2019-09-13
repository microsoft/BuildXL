// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using Xunit;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Results;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions
{
    public abstract class MemoizationSessionTests : MemoizationSessionTestBase
    {
        private const HashType ContentHashType = HashType.Vso0;
        private const int RandomContentByteCount = 100;

        protected MemoizationSessionTests(Func<IAbsFileSystem> createFileSystemFunc, ILogger logger)
            : base(createFileSystemFunc, logger)
        {
        }

        protected override Task RunTestAsync(
            Context context, DisposableDirectory testDirectory, Func<IMemoizationStore, IMemoizationSession, Task> funcAsync, Func<DisposableDirectory, IMemoizationStore> createStoreFunc = null)
        {
            return RunTestAsync(
                context,
                testDirectory,
                async store =>
                {
                    var configuration = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1);
                    var configurationModel = new ConfigurationModel(configuration);

                    using (var contentStore = new FileSystemContentStore(
                        FileSystem, SystemClock.Instance, testDirectory.Path, configurationModel))
                    {
                        try
                        {
                            var startupContentStoreResult = await contentStore.StartupAsync(context);
                            startupContentStoreResult.ShouldBeSuccess();

                            var contentSessionResult = contentStore.CreateSession(context, Name, ImplicitPin.None);
                            contentSessionResult.ShouldBeSuccess();

                            var sessionResult = store.CreateSession(context, Name, contentSessionResult.Session);
                            sessionResult.ShouldBeSuccess();

                            using (var cacheSession = new OneLevelCacheSession(Name, ImplicitPin.None, sessionResult.Session, contentSessionResult.Session))
                            {
                                try
                                {
                                    var r = await cacheSession.StartupAsync(context);
                                    r.ShouldBeSuccess();

                                    await funcAsync(store, cacheSession);
                                }
                                finally
                                {
                                    var r = await cacheSession.ShutdownAsync(context);
                                    r.ShouldBeSuccess();
                                }
                            }
                        }
                        finally
                        {
                            var shutdownContentStoreResult = await contentStore.ShutdownAsync(context);
                            shutdownContentStoreResult.ShouldBeSuccess();
                        }
                    }
                },
                createStoreFunc);
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
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();

            return RunTestAsync(context, async session =>
            {
                var putResult = await ((ICacheSession)session).PutRandomAsync(
                    context, ContentHashType, false, RandomContentByteCount, Token);
                var contentHashList = new ContentHashList(new[] {putResult.ContentHash});
                var addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, new ContentHashListWithDeterminism(contentHashList, Determinism[fromDeterminism]), Token).ShouldBeSuccess();
                Assert.Equal(Determinism[fromDeterminism].EffectiveGuid, addResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);

                // What we will do here is AddOrGet() a record that we already know is
                // there but with the determinism bit changed.
                addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, new ContentHashListWithDeterminism(contentHashList, Determinism[toDeterminism]), Token).ShouldBeSuccess();
                Assert.Null(addResult.ContentHashListWithDeterminism.ContentHashList);

                var getResult = await session.GetContentHashListAsync(context, strongFingerprint, Token).ShouldBeSuccess();
                Assert.Equal(
                    Determinism[shouldUpgrade ? toDeterminism : fromDeterminism].EffectiveGuid,
                    getResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);
            });
        }

        [Fact]
        public Task ChangingToolDeterministicFailsWhenPreviousContentExists()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();

            return RunTestAsync(context, async session =>
            {
                var putResult = await ((ICacheSession)session).PutRandomAsync(
                    context, ContentHashType, false, RandomContentByteCount, Token);
                var contentHashList = new ContentHashList(new[] {putResult.ContentHash});
                var addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.Tool), Token);
                Assert.Equal(CacheDeterminism.Tool, addResult.ContentHashListWithDeterminism.Determinism);

                // Add a new, different ContentHashList
                var newContentHashList = ContentHashList.Random();
                addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, new ContentHashListWithDeterminism(newContentHashList, CacheDeterminism.Tool), Token);
                Assert.Equal(AddOrGetContentHashListResult.ResultCode.InvalidToolDeterminismError, addResult.Code);
                Assert.Equal(contentHashList, addResult.ContentHashListWithDeterminism.ContentHashList);
            });
        }
    }
}
