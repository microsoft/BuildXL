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

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public record RedisVolatileEventStorageConfiguration
    {
        public string KeyPrefix { get; init; } = "rwalog";

        public TimeSpan MaximumKeyLifetime { get; init; } = TimeSpan.FromDays(2);

        /// <summary>
        /// Connection string for the Redis instance
        /// </summary>
        /// <remarks>
        /// This is used only for testing
        /// </remarks>
        public string? ConnectionString { get; init; }
    }

    /// <summary>
    /// Structure of data in Redis is laid out as follows:
    /// 
    /// - All keys relevant to the current log are prefixed with the
    ///   <see cref="RedisVolatileEventStorageConfiguration.KeyPrefix"/> in the configuration.
    ///   - This is meant to aid epoch changes, multiple instances sharing the same Redis, etc.
    /// - All keys have an expiry of <see cref="RedisVolatileEventStorageConfiguration.MaximumKeyLifetime"/>.
    ///   - This is meant to prevent bloat in case the feature stops being used, epoch changes, etc.
    ///   
    /// - Appending does an append to the key PREFIX:LogId:LogBlockId, and sets a hash key in PREFIX:LogEntryList
    /// - Reading reads directly from PREFIX:LogId:LogBlockId
    /// - Garbage collection reads from PREFIX:LogEntryList and removes all mentioned entries
    /// </summary>
    /// <remarks>
    /// This class implements both <see cref="IWriteAheadEventStorage"/> and <see cref="ICheckpointRegistry"/> because
    /// it is used as the checkpoint registry for the metadata service.
    /// </remarks>
    internal class RedisWriteAheadEventStorage : StartupShutdownSlimBase, IWriteAheadEventStorage, ICheckpointRegistry
    {
        private static readonly Regex GcListHashKeyRegex = new Regex(@"(?<logId>[0-9]+):(?<logBlockId>[0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        protected override Tracer Tracer { get; } = new Tracer(nameof(RedisWriteAheadEventStorage));

        /// <summary>
        /// Ownership of this class is shared between two different components, and so we need to allow multiple
        /// startups and shutdowns.
        /// </summary>
        public override bool AllowMultipleStartupAndShutdowns => true;

        private readonly IRedisDatabaseFactory? _redisDatabaseFactory;
        private RedisDatabaseAdapter? _redisDatabaseAdapter;

        private readonly RedisVolatileEventStorageConfiguration _configuration;
        private readonly IClock _clock;

        private readonly string _gcRedisKey;

        private readonly string _checkpointRegistryKey;

        public RedisWriteAheadEventStorage(RedisVolatileEventStorageConfiguration configuration, IRedisDatabaseFactory redisDatabaseFactory, IClock? clock = null)
            : this(configuration, clock)
        {
            _redisDatabaseFactory = redisDatabaseFactory;
        }

        /// <remarks>
        /// This is used only for testing
        /// </remarks>
        public RedisWriteAheadEventStorage(RedisVolatileEventStorageConfiguration configuration, RedisDatabaseAdapter redisDatabaseAdapter, IClock? clock = null)
            : this(configuration, clock)
        {
            _redisDatabaseAdapter = redisDatabaseAdapter;
        }

        private RedisWriteAheadEventStorage(RedisVolatileEventStorageConfiguration configuration, IClock? clock)
        {
            _configuration = configuration;
            _clock = clock ?? SystemClock.Instance;
            _gcRedisKey = $"{_configuration.KeyPrefix}:LogEntryList";
            _checkpointRegistryKey = $"{_configuration.KeyPrefix}:CheckpointRegistry";
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            // NOTE: _redisDatabaseFactory is only provided when running outside of tests.
            if (_redisDatabaseFactory != null)
            {
                _redisDatabaseAdapter = await _redisDatabaseFactory.CreateAsync(context, Tracer.Name, _configuration.ConnectionString!);
            }

            return BoolResult.Success;
        }

        public Task<BoolResult> AppendAsync(OperationContext context, BlockReference cursor, ReadOnlyMemory<byte> piece)
        {
            var msg = $"Cursor=[{cursor}] Length=[{piece.Length}]";
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var batch = _redisDatabaseAdapter!.CreateBatch(RedisOperation.All);

                var cursorKey = CreateCursorKey(cursor);
                _ = batch.AddOperation(string.Empty, async batch =>
                {
                    var addGcTask = batch.HashSetAsync(_gcRedisKey, CreateGcListHashKey(cursor), "");
                    var appendTask = batch.StringAppendAsync(cursorKey, piece);
                    await Task.WhenAll(addGcTask, appendTask);
                    return true;
                });

                _ = batch.AddOperation(string.Empty, batch => batch.KeyExpireAsync(cursorKey, _configuration.MaximumKeyLifetime));

                await _redisDatabaseAdapter!.ExecuteBatchOperationAsync(context, batch, context.Token).ThrowIfFailure();

                return BoolResult.Success;
            },
            traceOperationStarted: false,
            // Removing this (i.e., enabling logging on all operations) overwhelms NLog, causing extreme
            // memory usage growth until you run out of it.
            traceErrorsOnly: true,
            extraStartMessage: msg,
            extraEndMessage: _ => msg);
        }

        public Task<Result<Optional<ReadOnlyMemory<byte>>>> ReadAsync(OperationContext context, BlockReference cursor)
        {
            var msg = $"Cursor=[{cursor}]";
            return context.PerformOperationAsync(Tracer, async () =>
            {
                return await _redisDatabaseAdapter!.ExecuteBatchAsync(context, async batch =>
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
            var numPendingKeys = -1;
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var pendingKeys = await _redisDatabaseAdapter!.ExecuteBatchAsync(
                    context,
                    batch => batch.AddOperation(string.Empty, batch => batch.HashKeysAsync(_gcRedisKey)),
                    RedisOperation.All);
                numPendingKeys = pendingKeys.Length;

                await _redisDatabaseAdapter!.ExecuteBatchAsync(context, batch =>
                {
                    return batch.AddOperation(string.Empty, async batch =>
                    {
                        // NOTE: pendingKeys is always going to be small, because this method is called very
                        // frequently, so there are never too many entries in the hash table.
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
            extraEndMessage: _ => $"{msg} NumKeys=[{numPendingKeys}]");
        }

        private string CreateCursorKey(BlockReference cursor)
        {
            // NOTE: the adapter will prepend the key prefix to every operation anyways, but that key prefix is
            // controlled at the adapter level rather than in this component.
            return $"{_configuration.KeyPrefix}:{CreateGcListHashKey(cursor)}";
        }

        internal static bool TryParseGcListHashKey(string key, out BlockReference cursor)
        {
            var match = GcListHashKeyRegex.Match(key);
            if (!match.Success)
            {
                cursor = new BlockReference();
                return false;
            }

            Contract.Assert(match.Groups["logId"].Success, $"Could not match logId when parsing GC List Hash Key `{key}`");
            var logId = int.Parse(match.Groups["logId"].Value);

            Contract.Assert(match.Groups["logBlockId"].Success, $"Could not match logBlockId when parsing GC List Hash Key `{key}`");
            var logBlockId = int.Parse(match.Groups["logBlockId"].Value);

            cursor = new BlockReference()
            {
                LogId = new CheckpointLogId(logId),
                LogBlockId = logBlockId,
            };

            return true;
        }

        internal static string CreateGcListHashKey(BlockReference cursor)
        {
            return $"{cursor.LogId}:{cursor.LogBlockId}";
        }

        /// <remarks>
        /// This is not a record because .NET Core 3.1 does not support specifying constructors.
        /// 
        /// See: https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-immutability?pivots=dotnet-5-0
        /// </remarks>
        private class CheckpointRegistry
        {
            public string CheckpointId { get; set; } = string.Empty;

            public DateTime CreationTimeUtc { get; set; }

            public Result<string> ToJson()
            {
                try
                {
                    return Result.Success(JsonSerializer.Serialize(this));
                }
                catch (Exception e)
                {
                    return Result.FromException<string>(e);
                }
            }

            public static Result<CheckpointRegistry> FromJson(string json)
            {
                try
                {
                    var registry = JsonSerializer.Deserialize<CheckpointRegistry>(json);
                    return Result.Success(registry!);
                }
                catch (Exception e)
                {
                    return Result.FromException<CheckpointRegistry>(e, $"Failed to deserialize {nameof(CheckpointRegistry)} from json string `{json}`");
                }
            }
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

                var serializedRegistry = registry.ToJson().ThrowIfFailure();

                await _redisDatabaseAdapter!.ExecuteBatchAsync(
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
                var serializedRegistry = await _redisDatabaseAdapter!.ExecuteBatchAsync(
                    context,
                    batch => batch.AddOperation(string.Empty, batch => batch.StringGetAsync(_checkpointRegistryKey)),
                    RedisOperation.All);

                if (serializedRegistry.IsNullOrEmpty)
                {
                    return CheckpointState.CreateUnavailable(default);
                }

                var registry = CheckpointRegistry.FromJson(serializedRegistry).ThrowIfFailure();

                var checkpointState = new CheckpointState(
                    startSequencePoint: EventSequencePoint.Invalid,
                    registry.CheckpointId,
                    registry.CreationTimeUtc,
                    producer: default);

                return Result.Success(checkpointState);
            });
        }
    }
}
