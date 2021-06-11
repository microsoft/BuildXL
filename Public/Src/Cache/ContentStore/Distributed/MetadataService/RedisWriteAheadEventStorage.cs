// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public record RedisVolatileEventStorageConfiguration
    {
        public string KeyPrefix { get; init; } = "rwalog";

        public TimeSpan MaximumKeyLifetime { get; init; } = TimeSpan.FromDays(2);

        public string ConnectionString { get; init; }
    }

    /// <summary>
    /// Structure of data in Redis is laid out as follows:
    /// 
    /// - All keys relevant to the current log are prefixed with the KeyPrefix in the configuration.
    ///   - This is meant to aid epoch changes, multiple instances sharing the same Redis, etc.
    /// - All keys have an expiry of MaximumKeyLifetime.
    ///   - This is meant to prevent bloat in case the feature stops being used, epoch changes, etc.
    ///   
    /// - Appending does an append to the key PREFIX:LogId:LogBlockId, and sets a hash key in PREFIX:LogEntryList
    /// - Reading reads directly from PREFIX:LogId:LogBlockId
    /// - Garbage collection reads from PREFIX:LogEntryList and removes all mentioned entries
    /// </summary>
    internal class RedisWriteAheadEventStorage : StartupShutdownSlimBase, IWriteAheadEventStorage, ICheckpointRegistry
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(RedisWriteAheadEventStorage));

        public override bool AllowMultipleStartupAndShutdowns => true;

        private readonly IRedisDatabaseFactory _redisDatabaseFactory;
        private RedisDatabaseAdapter _redisDatabaseAdapter;
        private readonly RedisVolatileEventStorageConfiguration _configuration;
        private readonly IClock _clock;

        private readonly string _gcRedisKey;

        private readonly string _checkpointRegistryKey;

        public RedisWriteAheadEventStorage(RedisVolatileEventStorageConfiguration configuration, IRedisDatabaseFactory redisDatabaseFactory, IClock clock = null)
            : this(configuration, clock)
        {
            _redisDatabaseFactory = redisDatabaseFactory;
        }

        public RedisWriteAheadEventStorage(RedisVolatileEventStorageConfiguration configuration, RedisDatabaseAdapter redisDatabaseAdapter, IClock clock = null)
            : this(configuration, clock)
        {
            _redisDatabaseAdapter = redisDatabaseAdapter;
        }

        private RedisWriteAheadEventStorage(RedisVolatileEventStorageConfiguration configuration, IClock clock = null)
        {
            _configuration = configuration;
            _clock = clock ?? SystemClock.Instance;
            _gcRedisKey = $"{_configuration.KeyPrefix}:LogEntryList";
            _checkpointRegistryKey = $"{_configuration.KeyPrefix}:CheckpointRegistry";
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            if (_redisDatabaseFactory != null)
            {
                await _redisDatabaseFactory.StartupAsync(context).ThrowIfFailureAsync();

                _redisDatabaseAdapter = await _redisDatabaseFactory.CreateAsync(context, Tracer.Name, _configuration.ConnectionString);
            }

            return BoolResult.Success;
        }

        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            if (_redisDatabaseFactory != null)
            {
                return _redisDatabaseFactory.ShutdownAsync(context);
            }
            return base.ShutdownCoreAsync(context);
        }

        public Task<BoolResult> AppendAsync(OperationContext context, BlockReference cursor, ReadOnlyMemory<byte> piece)
        {
            var msg = $"Cursor=[{cursor}] Length=[{piece.Length}]";
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var batch = _redisDatabaseAdapter.CreateBatch(RedisOperation.All);

                var cursorKey = CreateCursorKey(cursor);
                _ = batch.AddOperation(string.Empty, async batch =>
                {
                    var addGcTask = batch.HashSetAsync(_gcRedisKey, CreateGcListHashKey(cursor), "");
                    var appendTask = batch.StringAppendAsync(cursorKey, piece);
                    await Task.WhenAll(addGcTask, appendTask);
                    return true;
                });

                _ = batch.AddOperation(string.Empty, batch => batch.KeyExpireAsync(cursorKey, _configuration.MaximumKeyLifetime));

                await _redisDatabaseAdapter.ExecuteBatchOperationAsync(context, batch, context.Token).ThrowIfFailure();

                return BoolResult.Success;
            },
            traceOperationStarted: false,
            extraStartMessage: msg,
            extraEndMessage: _ => msg);
        }

        public Task<Result<Optional<ReadOnlyMemory<byte>>>> ReadAsync(OperationContext context, BlockReference cursor)
        {
            var msg = $"Cursor=[{cursor}]";
            return context.PerformOperationAsync(Tracer, async () =>
            {
                return await _redisDatabaseAdapter.ExecuteBatchAsync(context, async batch =>
                {
                    var cursorKey = CreateCursorKey(cursor);

                    var dataTask = batch.StringGetAsync(cursorKey);
                    var expireTask = batch.KeyExpireAsync(cursorKey, _configuration.MaximumKeyLifetime);

                    await Task.WhenAll(dataTask, expireTask);

                    var data = await dataTask;
                    if (data.IsNullOrEmpty)
                    {
                        return Result.Success(Optional<ReadOnlyMemory<byte>>.Empty);
                    }

                    return Result.Success<Optional<ReadOnlyMemory<byte>>>((ReadOnlyMemory<byte>)data);
                }, RedisOperation.All);
            },
            traceOperationStarted: false,
            extraStartMessage: msg,
            extraEndMessage: _ => msg);
        }

        public Task<BoolResult> GarbageCollectAsync(OperationContext context, BlockReference acknowledgedCursor)
        {
            var msg = $"Cursor=[{acknowledgedCursor}]";
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var pendingKeys = await _redisDatabaseAdapter.ExecuteBatchAsync(
                    context,
                    batch => batch.AddOperation(string.Empty, batch => batch.HashKeysAsync(_gcRedisKey)),
                    RedisOperation.All);

                await _redisDatabaseAdapter.ExecuteBatchAsync(context, batch =>
                {
                    return batch.AddOperation(string.Empty, async batch =>
                    {
                        var tasks = new List<Task>(capacity: 2 * pendingKeys.Length);

                        foreach (var key in pendingKeys)
                        {
                            if (TryParseGcListHashKey(key, out var keyCursor))
                            {
                                if (acknowledgedCursor.IsGreaterThan(keyCursor))
                                {
                                    tasks.Add(batch.HashDeleteAsync(_gcRedisKey, key));
                                    tasks.Add(batch.KeyDeleteAsync(CreateCursorKey(keyCursor)));
                                }
                            }
                            else
                            {
                                // Could not parse, delete from the hash. If there was an associated key, it'll be removed
                                // in time.
                                tasks.Add(batch.HashDeleteAsync(_gcRedisKey, key));
                            }
                        }

                        await Task.WhenAll(tasks);

                        return true;
                    });
                }, RedisOperation.All);

                return BoolResult.Success;
            },
            traceOperationStarted: false,
            extraStartMessage: msg,
            extraEndMessage: _ => msg);
        }

        private string CreateCursorKey(BlockReference cursor)
        {
            // TODO: Prefix is typically done at the db level
            return $"{_configuration.KeyPrefix}:{CreateGcListHashKey(cursor)}";
        }

        private static readonly Regex GcListHashKeyRegex = new Regex(@"(?<logId>[0-9]+):(?<logBlockId>[0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private bool TryParseGcListHashKey(string key, out BlockReference cursor)
        {
            var match = GcListHashKeyRegex.Match(key);
            if (!match.Success)
            {
                cursor = new BlockReference();
                return false;
            }

            Contract.Assert(match.Groups["logId"].Success);
            var logId = int.Parse(match.Groups["logId"].Value);

            Contract.Assert(match.Groups["logBlockId"].Success);
            var logBlockId = int.Parse(match.Groups["logBlockId"].Value);

            cursor = new BlockReference()
            {
                LogId = new CheckpointLogId(logId),
                LogBlockId = logBlockId,
            };

            return true;
        }

        private string CreateGcListHashKey(BlockReference cursor)
        {
            return $"{cursor.LogId}:{cursor.LogBlockId}";
        }

        private class CheckpointRegistry
        {
            public string CheckpointId { get; set; }
            public DateTime CreationTimeUtc { get; set; }
        }

        public Task<BoolResult> RegisterCheckpointAsync(OperationContext context, string checkpointId, EventSequencePoint sequencePoint)
        {
            var msg = $"CheckpointId=[{checkpointId}] SequencePoint=[{sequencePoint}]";

            return context.PerformOperationAsync(Tracer, async () =>
            {
                var registry = new CheckpointRegistry()
                {
                    CheckpointId = checkpointId,
                    CreationTimeUtc = _clock.UtcNow
                };
                var serializedRegistry = JsonSerializer.Serialize(registry);

                await _redisDatabaseAdapter.ExecuteBatchAsync(
                    context,
                    batch => batch.AddOperation(string.Empty, batch => batch.StringSetAsync(_checkpointRegistryKey, serializedRegistry)),
                    RedisOperation.All);

                return BoolResult.Success;
            },
            extraStartMessage: msg,
            extraEndMessage: _ => msg);
        }

        public Task<Result<CheckpointState>> GetCheckpointStateAsync(OperationContext context)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var serializedRegistry = await _redisDatabaseAdapter.ExecuteBatchAsync(
                    context,
                    batch => batch.AddOperation(string.Empty, batch => batch.StringGetAsync(_checkpointRegistryKey)),
                    RedisOperation.All);

                if (serializedRegistry.IsNullOrEmpty)
                {
                    return CheckpointState.CreateUnavailable(Role.Worker, default, default);
                }

                var registry = JsonSerializer.Deserialize<CheckpointRegistry>(serializedRegistry);

                var checkpointState = new CheckpointState(
                    Role.Worker,
                    default,
                    registry.CheckpointId,
                    registry.CreationTimeUtc,
                    new MachineLocation(),
                    new MachineLocation());

                return Result.Success(checkpointState);
            });
        }
    }
}
