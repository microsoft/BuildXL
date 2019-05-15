// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using ContentStoreTest.Test;
using FluentAssertions;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions;
using Xunit;
using Xunit.Abstractions;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    public abstract class LocalCacheTests : CacheTests
    {
        private const HashType ContentHashType = HashType.Vso0;
        protected const uint MaxContentHashListItems = 10000;
        protected static readonly IClock Clock = new MemoryClock();

        protected virtual async Task<GetStatsResult> GetStatsResult(ICache cache, Context context)
        {
            var result = await cache.GetStatsAsync(context);
            result.ShouldBeSuccess();
            return result;
        }

        protected virtual async Task<long> GetCounterValue(string name, ICache cache, Context context)
        {
            var result = await GetStatsResult(cache, context);
            return result.CounterSet.GetIntegralWithNameLike(name);
        }

        protected LocalCacheTests(ITestOutputHelper output = null)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, output)
        {
        }

        [Fact]
        protected abstract Task ContentStoreStartupFails();

        [Fact]
        protected abstract Task MemoizationStoreStartupFails();

        [Fact]
        public virtual async Task ConcurrentCaches()
        {
            var context = new Context(Logger);
            using (var testDirectory1 = new DisposableDirectory(FileSystem))
            using (var testDirectory2 = new DisposableDirectory(FileSystem))
            {
                using (var cache1 = CreateCache(testDirectory1))
                using (var cache2 = CreateCache(testDirectory2))
                {
                    await cache1.StartupAsync(context).ShouldBeSuccess();
                    await cache2.StartupAsync(context).ShouldBeSuccess();
                    await cache1.ShutdownAsync(context).ShouldBeSuccess();
                    await cache2.ShutdownAsync(context).ShouldBeSuccess();
                }
            }
        }

        [Fact]
        public virtual async Task IdIsPersistent()
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                Guid cacheGuid;
                using (var cache = CreateCache(testDirectory))
                {
                    await cache.StartupAsync(context).ShouldBeSuccess();
                    cacheGuid = cache.Id;
                    await cache.ShutdownAsync(context).ShouldBeSuccess();
                }

                using (var cache = CreateCache(testDirectory))
                {
                    await cache.StartupAsync(context).ShouldBeSuccess();
                    cache.Id.Should().Be(cacheGuid);
                    await cache.ShutdownAsync(context).ShouldBeSuccess();
                }
            }
        }

        [Fact]
        public Task PinCallCounterBumpedOnUse()
        {
            return RunCacheAndSessionTestAsync(async (cache, session, context) =>
            {
                // TODO: PinAsync may fail here! Ignoring the result instead of expecting it to succeed.
                await session.PinAsync(context, ContentHash.Random(), Token).IgnoreFailure();
                await VerifyPinCallCounterBumpedOnUse(cache, context);
            });
        }

        protected abstract Task VerifyPinCallCounterBumpedOnUse(ICache cache, Context context);

        [Fact]
        public Task OpenStreamCallCounterBumpedOnUse()
        {
            return RunCacheAndSessionTestAsync(async (cache, session, context) =>
            {
                await session.OpenStreamAsync(context, ContentHash.Random(), Token).ShouldNotBeError();
                await VerifyOpenStreamCallCounterBumpedOnUse(cache, context);
            });
        }

        protected abstract Task VerifyOpenStreamCallCounterBumpedOnUse(ICache cache, Context context);

        [Fact]
        public Task PlaceFileCallCounterBumpedOnUse()
        {
            return RunCacheAndSessionTestAsync(async (cache, session, context) =>
            {
                await session.PlaceFileAsync
                    (
                        context,
                        ContentHash.Random(),
                        new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("C", "noexist")),
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.FailIfExists,
                        FileRealizationMode.Any,
                        Token
                    ).ShouldBeError();
                await VerifyPlaceFileCallCounterBumpedOnUse(cache, context);
            });
        }

        protected abstract Task VerifyPlaceFileCallCounterBumpedOnUse(ICache cache, Context context);

        [Fact]
        public Task PutFileCallCounterBumpedOnUse()
        {
            return RunCacheAndSessionTestAsync(async (cache, session, context) =>
            {
                await session.PutFileAsync(context, ContentHashType, new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("C", "noexist")), FileRealizationMode.Any, Token).ShouldBeError();
                await VerifyPutFileCallCounterBumpedOnUse(cache, context);
            });
        }

        protected abstract Task VerifyPutFileCallCounterBumpedOnUse(ICache cache, Context context);

        [Fact]
        public Task PutStreamCallCounterBumpedOnUse()
        {
            return RunCacheAndSessionTestAsync(async (cache, session, context) =>
            {
                var data = ThreadSafeRandom.GetBytes(7);
                using (var stream = new MemoryStream(data))
                {
                    await session.PutStreamAsync(context, ContentHashType, stream, Token).ShouldBeSuccess();
                    await VerifyPutStreamCallCounterBumpedOnUse(cache, context);
                }
            });
        }

        protected abstract Task VerifyPutStreamCallCounterBumpedOnUse(ICache cache, Context context);

        [Fact]
        public Task GetSelectorsCallCounterBumpedOnUse()
        {
            return RunCacheAndSessionTestAsync(async (cache, session, context) =>
            {
                Async::System.Collections.Generic.IAsyncEnumerable<GetSelectorResult> enumerator = session.GetSelectors(context, Fingerprint.Random(), Token);
                await enumerator.ToList(CancellationToken.None);
                long counter = await GetCounterValue("GetSelectorsCall", cache, context);
                counter.Should().Be(1);
            });
        }

        [Fact]
        public Task GetContentHashListCallCounterBumpedOnUse()
        {
            return RunCacheAndSessionTestAsync(async (cache, session, context) =>
            {
                await session.GetContentHashListAsync(context, StrongFingerprint.Random(), Token).ShouldBeSuccess();
                long counter = await GetCounterValue("GetContentHashListCall", cache, context);
                counter.Should().Be(1);
            });
        }

        [Fact]
        public Task AddOrGetContentHashListCallCounterBumpedOnUse()
        {
            return RunCacheAndSessionTestAsync(async (cache, session, context) =>
            {
                (await session.AddOrGetContentHashListAsync(
                    context,
                    StrongFingerprint.Random(),
                    new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None),
                    Token)).ShouldBeSuccess();
                long counter = await GetCounterValue("AddOrGetContentHashListCall", cache, context);
                counter.Should().Be(1);
            });
        }

        private async Task RunCacheAndSessionTestAsync(Func<ICache, ICacheSession, Context, Task> funcAsync)
        {
            var context = new Context(Logger);
            await RunTestAsync(context, async cache =>
            {
                var createSessionResult = cache.CreateSession(context, context.Id.ToString(), ImplicitPin.None);
                createSessionResult.ShouldBeSuccess();

                using (ICacheSession session = createSessionResult.Session)
                {
                    try
                    {
                        var startupResult = await session.StartupAsync(context);
                        startupResult.ShouldBeSuccess();

                        await funcAsync(cache, session, context);
                    }
                    finally
                    {
                        var shutdownResult = await session.ShutdownAsync(context);
                        shutdownResult.ShouldBeSuccess();
                    }
                }
            });
        }
    }
}
