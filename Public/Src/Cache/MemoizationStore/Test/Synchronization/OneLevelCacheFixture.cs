// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Tracing;
using ContentStoreTest.Test;

namespace BuildXL.Cache.MemoizationStore.Test.Synchronization
{
    /// <summary>
    /// Using Fixture pattern to separate secondary test logic (like initialization) from the main logic.
    /// </summary>
    public class OneLevelCacheFixture
    {
        private Func<(BoolResult contentStoreResult, BoolResult memoizationStoreResult)> _createAndStartStoreFunc;

        public OneLevelCacheFixture()
        {
            _createAndStartStoreFunc = () => (ContentStore.StartupAsyncResult, MemoizationStore.StartupAsyncResult);

            ContentStore = new MockContentStore();
            MemoizationStore = new MockMemoizationStore();
            Context = new Context(TestGlobal.Logger);
        }

        /// <summary>
        /// Creates an instance of a system under test.
        /// </summary>
        public OneLevelCacheBase CreateSut()
        {
            return new OneLevelCacheMock(ContentStore, MemoizationStore, _createAndStartStoreFunc);
        }

        /// <nodoc />
        public Context Context { get; }

        /// <nodoc />
        public MockContentStore ContentStore { get; private set; }

        /// <nodoc />
        public MockMemoizationStore MemoizationStore { get; private set; }

        /// <nodoc />
        public OneLevelCacheFixture WithContentStore(MockContentStore contentStore)
        {
            ContentStore = contentStore;
            return this;
        }

        /// <nodoc />
        public OneLevelCacheFixture WithMemoizationStore(MockMemoizationStore memoizationStore)
        {
            MemoizationStore = memoizationStore;
            return this;
        }

        /// <nodoc />
        public OneLevelCacheFixture WithContentStoreStartupResult(BoolResult contentStoreStartupResult)
        {
            ContentStore ??= new MockContentStore();
            ContentStore.StartupAsyncResult = contentStoreStartupResult;
            return this;
        }

        /// <nodoc />
        public OneLevelCacheFixture WithMemoizationStoreStartupResult(BoolResult memoizationStoreStartupResult)
        {
            MemoizationStore ??= new MockMemoizationStore();
            MemoizationStore.StartupAsyncResult = memoizationStoreStartupResult;
            return this;
        }

        /// <nodoc />
        public OneLevelCacheFixture WithContentStoreShutdownResult(BoolResult contentStoreShutdownResult)
        {
            ContentStore ??= new MockContentStore();
            ContentStore.ShutdownAsyncResult = contentStoreShutdownResult;
            return this;
        }

        /// <nodoc />
        public OneLevelCacheFixture WithMemoizationStoreShutdownResult(BoolResult memoizationStoreShutdownResult)
        {
            MemoizationStore ??= new MockMemoizationStore();
            MemoizationStore.ShutdownAsyncResult = memoizationStoreShutdownResult;
            return this;
        }

        /// <nodoc />
        public OneLevelCacheFixture WithCreateAndStartStoreFunc(
            Func<(BoolResult contentStoreResult, BoolResult memoizationStoreResult)> func)
        {
            _createAndStartStoreFunc = func;
            return this;
        }

        /// <summary>
        /// The mock type that derives from <see cref="OneLevelCacheBase"/>.
        /// </summary>
        public class OneLevelCacheMock : OneLevelCacheBase
        {
            private readonly MockContentStore _contentStore;
            private readonly MockMemoizationStore _memoizationStore;
            private readonly Func<(BoolResult contentStoreResult, BoolResult memoizationStoreResult)> _createAndStartStoresFunc;

            public OneLevelCacheMock(
                MockContentStore contentStore,
                MockMemoizationStore memoizationStore,
                Func<(BoolResult contentStoreResult, BoolResult memoizationStoreResult)> createAndStartStoresFunc)
                : base(new OneLevelCacheBaseConfiguration(Guid.NewGuid(), PassContentToMemoization: false))
            {
                _contentStore = contentStore;
                _memoizationStore = memoizationStore;
                _createAndStartStoresFunc = createAndStartStoresFunc;
            }

            protected override CacheTracer CacheTracer { get; } = new CacheTracer(nameof(OneLevelCacheMock));

            protected override Task<(BoolResult contentStoreResult, BoolResult memoizationStoreResult)> CreateAndStartStoresAsync(OperationContext context)
            {
                ContentStore = _contentStore;
                MemoizationStore = _memoizationStore;
                return Task.FromResult(_createAndStartStoresFunc());
            }
        }

        /// <summary>
        /// Base class for implementing mock classes for the stores.
        /// </summary>
        public abstract class StartupShutdownMock : StartupShutdownBase
        {
            public BoolResult StartupAsyncResult { get; set; } = BoolResult.Success;
            public BoolResult ShutdownAsyncResult { get; set; } = BoolResult.Success;

            public override bool StartupCompleted => true;

            protected override Tracer Tracer { get; } = new Tracer("mock");

            protected override Task<BoolResult> StartupCoreAsync(OperationContext context) => Task.FromResult(StartupAsyncResult);

            protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context) => Task.FromResult(ShutdownAsyncResult);
        }

        /// <summary>
        /// The mock implementation for <see cref="IContentStore"/> interface.
        /// </summary>
        public class MockContentStore : StartupShutdownMock, IContentStore
        {
            public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin) => null;

            public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin) => null;

            public Task<GetStatsResult> GetStatsAsync(Context context) => Task.FromResult(new GetStatsResult(new CounterSet()));

            public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions deleteOptions) => null;

            public void PostInitializationCompleted(Context context, BoolResult result)
            {
            }
        }

        /// <summary>
        /// The mock implementation for <see cref="IMemoizationStore"/> interface.
        /// </summary>
        public class MockMemoizationStore : StartupShutdownMock, IMemoizationStore
        {
            public CreateSessionResult<IReadOnlyMemoizationSession> CreateReadOnlySession(Context context, string name) => null;

            public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name) => null;

            public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name, IContentSession contentSession) => null;

            public Task<GetStatsResult> GetStatsAsync(Context context) => Task.FromResult(new GetStatsResult(new CounterSet()));

            public IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context) => null;
        }
    }
}
