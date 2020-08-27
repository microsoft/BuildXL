// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.Host.Service.Internal
{
    // TODO: move it to the library?
    /// <summary>
    /// Store which aggregates a local and backing content store. The backing content store is
    /// used to populate local content store in cases of local misses.
    /// </summary>
    public class MultiLevelContentStore : StartupShutdownBase, IContentStore
    {
        private readonly IContentStore _localContentStore;
        private readonly IContentStore _backingContentStore;

        protected override Tracer Tracer { get; } = new ContentStoreTracer(nameof(MultiLevelContentStore));

        public MultiLevelContentStore(
            IContentStore localContentStore,
            IContentStore backingContentStore)

        {
            Contract.RequiresNotNull(localContentStore);
            Contract.RequiresNotNull(backingContentStore);

            _localContentStore = localContentStore;
            _backingContentStore = backingContentStore;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            return (await _localContentStore.StartupAsync(context) & await _backingContentStore.StartupAsync(context));
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            return (await _localContentStore.ShutdownAsync(context) & await _backingContentStore.ShutdownAsync(context));
        }

        protected override void DisposeCore()
        {
            _localContentStore.Dispose();
            _backingContentStore.Dispose();
        }

        /// <nodoc />
        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateReadOnlySessionCall.Run((ContentStoreTracer)Tracer, new OperationContext(context), name, () =>
            {
                var localSession = _localContentStore.CreateSession(context, name, implicitPin).ThrowIfFailure();
                var backingSession = _backingContentStore.CreateReadOnlySession(context, name, implicitPin).ThrowIfFailure();

                return new CreateSessionResult<IReadOnlyContentSession>(new MultiLevelReadOnlyContentSession<IReadOnlyContentSession>(name, localSession.Session, backingSession.Session, isLocalWritable: true));
            });
        }

        /// <nodoc />
        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateSessionCall.Run((ContentStoreTracer)Tracer, new OperationContext(context), name, () =>
            {
                var localSession = _localContentStore.CreateSession(context, name, implicitPin).ThrowIfFailure();
                var backingSession = _backingContentStore.CreateSession(context, name, implicitPin).ThrowIfFailure();

                return new CreateSessionResult<IContentSession>(new MultiLevelContentSession(name, localSession.Session, backingSession.Session));
            });
        }

        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            var operationContext = new OperationContext(context);
            return operationContext.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    CounterSet aggregatedCounters = new CounterSet();
                    var localStats = await _localContentStore.GetStatsAsync(context).ThrowIfFailure();
                    var backingStats = await _backingContentStore.GetStatsAsync(context).ThrowIfFailure();

                    aggregatedCounters.Merge(localStats.Value, "Local");
                    aggregatedCounters.Merge(backingStats.Value, "Backing");
                    return new GetStatsResult(aggregatedCounters);
                });
        }

        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions deleteOptions)
        {
            throw new NotImplementedException();
        }

        public void PostInitializationCompleted(Context context, BoolResult result)
        {
        }
    }
}
