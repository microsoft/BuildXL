// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions.Internal;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// Store which aggregates a local and backing content store which also represents local content. This is used to ensure
    /// that content is deduplicated as hardlinks between local and backing store.
    /// </summary>
    public class BackedFileSystemContentStore : StartupShutdownBase, IContentStore, IStreamStore
    {
        public override bool AllowMultipleStartupAndShutdowns => true;

        private readonly IAbsFileSystem _fileSystem;
        private readonly FileSystemContentStore _localContentStore;
        private readonly IContentStore _backingContentStore;

        protected override Tracer Tracer { get; } = new ContentStoreTracer(nameof(BackedFileSystemContentStore));

        public BackedFileSystemContentStore(
            IAbsFileSystem fileSystem,
            FileSystemContentStore localContentStore,
            IContentStore backingContentStore)

        {
            Contract.RequiresNotNull(localContentStore);
            Contract.RequiresNotNull(backingContentStore);
            _fileSystem = fileSystem;
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
        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateSessionCall.Run((ContentStoreTracer)Tracer, new OperationContext(context), name, () =>
            {
                var localSession = _localContentStore.CreateSession(context, name, implicitPin).ThrowIfFailure();
                var backingSession = _backingContentStore.CreateSession(context, name, implicitPin).ThrowIfFailure();

                return new CreateSessionResult<IContentSession>(
                    new BackedFileSystemContentSession(
                        name,
                        _fileSystem,
                        _localContentStore,
                        (ITrustedContentSession)localSession.Session!,
                        backingSession.Session!));
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

                    aggregatedCounters.Merge(localStats.Value, "");
                    aggregatedCounters.Merge(backingStats.Value, "Backing.");
                    return new GetStatsResult(aggregatedCounters);
                });
        }

        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions? deleteOptions)
        {
            return _localContentStore.DeleteAsync(context, contentHash, deleteOptions);
        }

        public void PostInitializationCompleted(Context context)
        {
            _localContentStore.PostInitializationCompleted(context);
        }

        public Task<OpenStreamResult> StreamContentAsync(Context context, ContentHash contentHash)
        {
            return _localContentStore.StreamContentAsync(context, contentHash);
        }
    }
}