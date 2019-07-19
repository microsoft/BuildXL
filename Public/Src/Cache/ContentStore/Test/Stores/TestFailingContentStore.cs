// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace ContentStoreTest.Stores
{
    /// <summary>
    ///     Test class for a store that always fails.
    /// </summary>
    internal class TestFailingContentStore : IContentStore
    {
        internal const string FailureMessage = "Test failure message from a store that always fails.";

        // <inheritdoc />
        public bool StartupCompleted { get; }

        // <inheritdoc />
        public bool StartupStarted { get; private set; }

        // <inheritdoc />
        public Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;
            return Task.FromResult(new BoolResult(FailureMessage));
        }

        // <inheritdoc />
        public void Dispose()
        {
        }

        // <inheritdoc />
        public bool ShutdownCompleted { get; }

        // <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        // <inheritdoc />
        public Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;
            return Task.FromResult(new BoolResult(FailureMessage));
        }

        // <inheritdoc />
        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return new CreateSessionResult<IReadOnlyContentSession>(FailureMessage);
        }

        // <inheritdoc />
        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return new CreateSessionResult<IContentSession>(FailureMessage);
        }

        // <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return Task.FromResult(new GetStatsResult(FailureMessage));
        }

        /// <inheritdoc />
        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash)
        {
            return Task.FromResult(new DeleteResult(DeleteResult.ResultCode.ContentNotDeleted, FailureMessage));
        }

        /// <inheritdoc />
        public void PostInitializationCompleted(Context context, BoolResult result) { }
    }
}
