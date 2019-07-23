// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
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
    public class RocksDbMemoizationStore : StartupShutdownBase, IMemoizationStore
    {
        private const string Component = nameof(RocksDbMemoizationStore);
        
        private IClock _clock;
        
        private RocksDbContentLocationDatabase _database;

        /// <summary>
        ///     Store tracer.
        /// </summary>
        private readonly MemoizationStoreTracer _tracer;

        /// <inheritdoc />
        protected override Tracer Tracer => _tracer;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RocksDbMemoizationStore"/> class.
        /// </summary>
        public RocksDbMemoizationStore(ILogger logger, IClock clock, RocksDbMemoizationStoreConfiguration config)
        {
            Contract.Requires(logger != null);
            Contract.Requires(config != null);
            Contract.Requires(clock != null);

            _tracer = new MemoizationStoreTracer(logger, Component);
            _clock = clock;
            _database = new RocksDbContentLocationDatabase(clock, config.Database, () => new MachineId[] { });
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
            return GetStatsCall<MemoizationStoreTracer>.RunAsync(_tracer, new OperationContext(context), () =>
            {
                var counters = new CounterSet();
                counters.Merge(_tracer.GetCounters(), $"{Component}.");
                return Task.FromResult(new GetStatsResult(counters));
            });
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            var cldbStartup = await _database.StartupAsync(context);
            if (!cldbStartup.Succeeded)
            {
                return cldbStartup;
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var cldbShutdown = await _database.ShutdownAsync(context);
            if (!cldbShutdown.Succeeded)
            {
                return cldbShutdown;
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        public Async::System.Collections.Generic.IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context)
        {
            var ctx = new OperationContext(context);
            return AsyncEnumerableExtensions.CreateSingleProducerTaskAsyncEnumerable<StructResult<StrongFingerprint>>(() => Task.FromResult(_database.EnumerateStrongFingerprints(ctx)));
        }

        internal Task<GetContentHashListResult> GetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, CancellationToken cts)
        {
            var ctx = new OperationContext(context, cts);
            return GetContentHashListCall.RunAsync(_tracer, ctx, strongFingerprint, async () => {
                return await Task.FromResult(_database.GetContentHashList(ctx, strongFingerprint));
            });
        }

        internal Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism contentHashListWithDeterminism, IContentSession contentSession, CancellationToken cts)
        {
            var ctx = new OperationContext(context, cts);
            return AddOrGetContentHashListCall.RunAsync(_tracer, ctx, strongFingerprint, async () => {
                return await ctx.PerformOperationAsync(
               _tracer,
               async () =>
               {
                   // We do multiple attempts here because we have a "CompareExchange" RocksDB in the heart
                   // of this implementation, and this may fail if the database is heavily contended.
                   // Unfortunately, there is not much we can do at the time of writing to avoid this
                   // requirement.
                   var maxAttempts = 5;
                   while (maxAttempts-- >= 0)
                   {
                       var contentHashList = contentHashListWithDeterminism.ContentHashList;
                       var determinism = contentHashListWithDeterminism.Determinism;

                       // Load old value. Notice that this get updates the time, regardless of whether we replace the value or not.
                       var oldContentHashListWithDeterminism = _database.GetContentHashList(ctx, strongFingerprint);
                       var oldContentHashList = oldContentHashListWithDeterminism.ContentHashListWithDeterminism.ContentHashList;
                       var oldDeterminism = oldContentHashListWithDeterminism.ContentHashListWithDeterminism.Determinism;

                       // Make sure we're not mixing SinglePhaseNonDeterminism records
                       if (!(oldContentHashList is null) &&
                                  oldDeterminism.IsSinglePhaseNonDeterministic != determinism.IsSinglePhaseNonDeterministic)
                       {
                           return AddOrGetContentHashListResult.SinglePhaseMixingError;
                       }

                       if (oldContentHashList is null || oldDeterminism.ShouldBeReplacedWith(determinism) ||
                                   !(await contentSession.EnsureContentIsAvailableAsync(ctx, oldContentHashList, ctx.Token).ConfigureAwait(false)))
                       {
                           // Replace if incoming has better determinism or some content for the existing
                           // entry is missing. The entry could have changed since we fetched the old value
                           // earlier, hence, we need to check it hasn't.
                           var exchanged = _database.CompareExchange(
                              ctx,
                              strongFingerprint,
                              oldContentHashListWithDeterminism.ContentHashListWithDeterminism,
                              contentHashListWithDeterminism).ThrowOnError();
                           if (!exchanged)
                           {
                               // Our update lost, need to retry
                               continue;
                           }

                           return new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(null, determinism));
                       }

                       // If we didn't accept the new value because it is the same as before, just with a not
                       // necessarily better determinism, then let the user know.
                       if (!(oldContentHashList is null) && oldContentHashList.Equals(contentHashList))
                       {
                           return new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(null, oldDeterminism));
                       }

                       // If we didn't accept a deterministic tool's data, then we're in an inconsistent state
                       if (determinism.IsDeterministicTool)
                       {
                           return new AddOrGetContentHashListResult(
                               AddOrGetContentHashListResult.ResultCode.InvalidToolDeterminismError,
                               oldContentHashListWithDeterminism.ContentHashListWithDeterminism);
                       }

                       // If we did not accept the given value, return the value in the cache
                       return new AddOrGetContentHashListResult(oldContentHashListWithDeterminism);
                   }

                   return new AddOrGetContentHashListResult("Hit too many races attempting to add content hash list into the cache");
                   // TODO(jubayard): decouple the counters from the ContentLocationDatabase
               }, _database.Counters[ContentLocationDatabaseCounters.AddOrGetContentHashList]);
            });
        }

        internal Task<Result<Selector[]>> GetSelectorsCoreAsync(Context context, Fingerprint weakFingerprint)
        {
            var ctx = new OperationContext(context);

            var stopwatch = new Stopwatch();
            try
            {
                _tracer.GetSelectorsStart(ctx, weakFingerprint);
                stopwatch.Start();

                var getSelectorsResult = new List<Selector>();
                foreach (var result in _database.GetSelectors(ctx, weakFingerprint))
                {
                    if (!result.Succeeded)
                    {
                        return Task.FromResult(Result.FromError<Selector[]>(result));
                    }

                    getSelectorsResult.Add(result.Selector);
                }

                _tracer.GetSelectorsCount(ctx, weakFingerprint, getSelectorsResult.Count);
                return Task.FromResult(Result.Success(getSelectorsResult.ToArray()));
            }
            catch (Exception exception)
            {
                _tracer.Debug(ctx, $"{Component}.GetSelectors() error=[{exception}]");
                return Task.FromResult(Result.FromException<Selector[]>(exception));
            }
            finally
            {
                stopwatch.Stop();
                _tracer.GetSelectorsStop(ctx, stopwatch.Elapsed);
            }
        }
    }
}
