// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace ContentStoreTest.Grpc
{
    internal class TestHangingContentStore : IContentStore
    {
        private readonly bool _useCancellationToken;
        private readonly SemaphoreSlim _hangHasStartedSemaphore;

        public TestHangingContentStore(bool useCancellationToken, SemaphoreSlim hangHasStartedSemaphore)
        {
            _useCancellationToken = useCancellationToken;
            _hangHasStartedSemaphore = hangHasStartedSemaphore;
        }

        public bool StartupCompleted { get; private set; }

        public bool StartupStarted { get; private set; }

        public Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;
            StartupCompleted = true;
            return Task.FromResult(BoolResult.Success);
        }

        public void Dispose()
        {
        }

        public bool ShutdownCompleted { get; private set; }

        public bool ShutdownStarted { get; private set; }

        public Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;
            ShutdownCompleted = true;
            return Task.FromResult(BoolResult.Success);
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return new CreateSessionResult<IReadOnlyContentSession>(new TestHangingContentSession(name, _useCancellationToken, _hangHasStartedSemaphore));
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return new CreateSessionResult<IContentSession>(new TestHangingContentSession(name, _useCancellationToken, _hangHasStartedSemaphore));
        }

        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return Task.FromResult(new GetStatsResult(new CounterSet()));
        }

        /// <inheritdoc />
        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public void PostInitializationCompleted(Context context, BoolResult result) { }
    }
}
