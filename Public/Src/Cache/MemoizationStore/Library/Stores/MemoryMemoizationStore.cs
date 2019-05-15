// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Tracing;

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <summary>
    ///     An IMemoizationStore implementation using memory.
    /// </summary>
    public sealed class MemoryMemoizationStore : StartupShutdownBase, IMemoizationStore
    {
        private const string Component = nameof(MemoryMemoizationStore);

        private readonly MemoizationStoreTracer _tracer;
        private readonly IList<Record> _records = new List<Record>();
        private readonly LockSet<Fingerprint> _lockSet = new LockSet<Fingerprint>();

        /// <inheritdoc />
        protected override Tracer Tracer => _tracer;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MemoryMemoizationStore"/> class.
        /// </summary>
        public MemoryMemoizationStore(ILogger logger)
        {
            Contract.Requires(logger != null);

            _tracer = new MemoizationStoreTracer(logger, nameof(MemoryMemoizationStore));
        }

        /// <inheritdoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            return Task.FromResult(BoolResult.Success);
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            return Task.FromResult(BoolResult.Success);
        }

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyMemoizationSession> CreateReadOnlySession(Context context, string name)
        {
            var session = new ReadOnlyMemoryMemoizationSession(name, this);
            return new CreateSessionResult<IReadOnlyMemoizationSession>(session);
        }

        /// <inheritdoc />
        public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name)
        {
            var session = new MemoryMemoizationSession(name, this);
            return new CreateSessionResult<IMemoizationSession>(session);
        }

        /// <inheritdoc />
        public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name, IContentSession contentSession)
        {
            var session = new MemoryMemoizationSession(name, this, contentSession);
            return new CreateSessionResult<IMemoizationSession>(session);
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return GetStatsCall<MemoizationStoreTracer>.RunAsync(
                _tracer, OperationContext(context), () => Task.FromResult(new GetStatsResult(_tracer.GetCounters())));
        }

        /// <summary>
        ///     Enumerate known selectors for a given weak fingerprint.
        /// </summary>
        internal Async::System.Collections.Generic.IAsyncEnumerable<GetSelectorResult> GetSelectors(Context context, Fingerprint weakFingerprint, CancellationToken cts)
        {
            var result = GetSelectorsCore(context, weakFingerprint, cts);
            if (result)
            {
                return result.Value.Select(r => new GetSelectorResult(r)).ToAsyncEnumerable();
            }

            return new[] {new GetSelectorResult(result)}.ToAsyncEnumerable();
        }

        /// <summary>
        ///     Enumerate known selectors for a given weak fingerprint.
        /// </summary>
        internal Result<Selector[]> GetSelectorsCore(Context context, Fingerprint weakFingerprint, CancellationToken cts)
        {
            var stopwatch = new Stopwatch();

            try
            {
                _tracer.GetSelectorsStart(context, weakFingerprint);
                stopwatch.Start();

                using (_lockSet.AcquireAsync(weakFingerprint).Result)
                {
                    Selector[] records = _records
                        .Where(r => r.StrongFingerprint.WeakFingerprint.Equals(weakFingerprint))
                        .Select(r => r.StrongFingerprint.Selector)
                        .ToArray();
                    return Result.Success(records);
                }
            }
            catch (Exception exception)
            {
                _tracer.Debug(context, $"{Component}.GetSelectors() error=[{exception}]");
                return Result.FromException<Selector[]>(exception);
            }
            finally
            {
                stopwatch.Stop();
                _tracer.GetSelectorsStop(context, stopwatch.Elapsed);
            }
        }

        /// <summary>
        ///     Load a ContentHashList.
        /// </summary>
        internal Task<GetContentHashListResult> GetContentHashListAsync(
            Context context, StrongFingerprint strongFingerprint, CancellationToken cts)
        {
            return GetContentHashListCall.RunAsync(_tracer, context, strongFingerprint, async () =>
            {
                using (await _lockSet.AcquireAsync(strongFingerprint.WeakFingerprint).ConfigureAwait(false))
                {
                    var record = _records.FirstOrDefault(r => r.StrongFingerprint == strongFingerprint);
                    return record == null
                        ? new GetContentHashListResult(new ContentHashListWithDeterminism(null, CacheDeterminism.None))
                        : new GetContentHashListResult(record.ContentHashListWithDeterminism);
                }
            });
        }

        /// <summary>
        ///     Store a ContentHashList
        /// </summary>
        internal async Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(
            Context context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism contentHashListWithDeterminism,
            IContentSession contentSession,
            CancellationToken cts)
        {
            using (var cancellableContext = TrackShutdown(context, cts))
            {
                return await AddOrGetContentHashListCall.RunAsync(_tracer, cancellableContext, strongFingerprint, async () =>
                {
                    using (await _lockSet.AcquireAsync(strongFingerprint.WeakFingerprint).ConfigureAwait(false))
                    {
                        var record = _records.FirstOrDefault(r => r.StrongFingerprint == strongFingerprint);

                        if (record == null)
                        {
                            // Match not found, add it.
                            record = new Record(strongFingerprint, contentHashListWithDeterminism);
                            _records.Add(record);
                        }
                        else
                        {
                            // Make sure we're not mixing SinglePhaseNonDeterminism records
                            if (record.ContentHashListWithDeterminism.Determinism.IsSinglePhaseNonDeterministic !=
                                contentHashListWithDeterminism.Determinism.IsSinglePhaseNonDeterministic)
                            {
                                return AddOrGetContentHashListResult.SinglePhaseMixingError;
                            }

                            // Match found.
                            // Replace if incoming has better determinism or some content for the existing entry is missing.
                            if (record.ContentHashListWithDeterminism.Determinism.ShouldBeReplacedWith(contentHashListWithDeterminism.Determinism) ||
                                !(await contentSession.EnsureContentIsAvailableAsync(context, record.ContentHashListWithDeterminism.ContentHashList, cts)
                                    .ConfigureAwait(false)))
                            {
                                _records.Remove(record);
                                record = new Record(record.StrongFingerprint, contentHashListWithDeterminism);
                                _records.Add(record);
                            }
                        }

                        // Accept the value if it matches the final value in the cache
                        if (contentHashListWithDeterminism.ContentHashList.Equals(record.ContentHashListWithDeterminism.ContentHashList))
                        {
                            return new AddOrGetContentHashListResult(
                                new ContentHashListWithDeterminism(null, record.ContentHashListWithDeterminism.Determinism));
                        }

                        // If we didn't accept a deterministic tool's data, then we're in an inconsistent state
                        if (contentHashListWithDeterminism.Determinism.IsDeterministicTool)
                        {
                            return new AddOrGetContentHashListResult(
                                AddOrGetContentHashListResult.ResultCode.InvalidToolDeterminismError, record.ContentHashListWithDeterminism);
                        }

                        // If we did not accept the given value, return the value in the cache
                        return new AddOrGetContentHashListResult(record.ContentHashListWithDeterminism);
                    }
                });
            }
        }

        /// <inheritdoc/>
        public Async::System.Collections.Generic.IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context)
        {
            return _records.Select(record => new StructResult<StrongFingerprint>(record.StrongFingerprint)).ToAsyncEnumerable();
        }
    }
}
