// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.SQLite;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Tracing;

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <summary>
    ///     An IMemoizationStore implementation using RocksDb.
    /// </summary>
    public class RocksDbMemoizationStore : IMemoizationStore
    {
        private const string Component = nameof(RocksDbMemoizationStore);

        private bool _disposed;
        private IClock _clock;
        private RocksDbContentLocationDatabaseConfiguration _config;
        private RocksDbContentLocationDatabase _cldb;

        /// <summary>
        ///     Store tracer.
        /// </summary>
        private readonly RocksDbMemoizationStoreTracer Tracer;

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <inheritdoc />
        public bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RocksDbMemoizationStore"/> class.
        /// </summary>
        public RocksDbMemoizationStore(ILogger logger, IClock clock, RocksDbContentLocationDatabaseConfiguration config)
        {
            Contract.Requires(logger != null);
            Contract.Requires(config != null);
            Contract.Requires(clock != null);

            Tracer = new RocksDbMemoizationStoreTracer(logger, Component);
            _clock = clock;
            _config = config;
            _cldb = new RocksDbContentLocationDatabase(clock, config, () => new MachineId[] { });
        }

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyMemoizationSession> CreateReadOnlySession(Context context, string name)
        {
            var session = new ReadOnlyRocksDbMemoizationSession(name, this);
            return new CreateSessionResult<IReadOnlyMemoizationSession>(session);
        }

        /// <inheritdoc />
        public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name)
        {
            var session = new RocksDbMemoizationSession(name, this);
            return new CreateSessionResult<IMemoizationSession>(session);
        }

        /// <inheritdoc />
        public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name, IContentSession contentSession)
        {
            var session = new RocksDbMemoizationSession(name, this, contentSession);
            return new CreateSessionResult<IMemoizationSession>(session);
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public async Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;

            var cldbStartup = await _cldb.StartupAsync(context);
            if (!cldbStartup.Succeeded)
            {
                return cldbStartup;
            }

            StartupCompleted = true;
            return BoolResult.Success;
        }

        /// <inheritdoc />
        public async Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;

            var cldbShutdown = await _cldb.ShutdownAsync(context);
            if (!cldbShutdown.Succeeded)
            {
                return cldbShutdown;
            }

            ShutdownCompleted = true;
            return BoolResult.Success;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Dispose(true);
            GC.SuppressFinalize(this);

            _disposed = true;
        }

        /// <inheritdoc />
        public void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
            }
        }

        /// <inheritdoc />
        public Async::System.Collections.Generic.IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context)
        {
            var ctx = new OperationContext(context);
            return AsyncEnumerableExtensions.CreateSingleProducerTaskAsyncEnumerable<StructResult<StrongFingerprint>>(() => Task.FromResult(_cldb.EnumerateStrongFingerprints(ctx)));
        }

        internal Task<GetContentHashListResult> GetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, CancellationToken cts)
        {
            // TODO: tracer
            var ctx = new OperationContext(context);
            return GetContentHashListCall.RunAsync(Tracer, context, strongFingerprint, () => {
                return Task.FromResult(_cldb.GetContentHashList(ctx, strongFingerprint));
            });
        }

        internal Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism contentHashListWithDeterminism, IContentSession contentSession, CancellationToken cts)
        {
            var ctx = new OperationContext(context);
            return AddOrGetContentHashListCall.RunAsync(Tracer, ctx, strongFingerprint, () => {
                // TODO: weave with content session for removing stuff
                return Task.FromResult(_cldb.AddOrGetContentHashList(ctx, strongFingerprint, contentHashListWithDeterminism));
            });
        }

        internal Task<Result<Selector[]>> GetSelectorsCoreAsync(Context context, Fingerprint weakFingerprint)
        {
            var results = new List<Selector>();

            var ctx = new OperationContext(context);
            foreach (var result in _cldb.GetSelectors(ctx, weakFingerprint))
            {
                if (!result.Succeeded)
                {
                    return Task.FromResult(Result.FromError<Selector[]>(result));
                }

                results.Add(result.Selector);
            }

            return Task.FromResult(Result.Success(results.ToArray()));
        }
    }
}
