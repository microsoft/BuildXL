// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Microsoft.VisualBasic;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.Distributed.Test.MetadataService
{
    [Collection("Redis-based tests")]
    public class ContentMetadataEventStreamTests : TestBase, IDisposable
    {
        protected Tracer Tracer { get; } = new Tracer(nameof(ContentMetadataEventStreamTests));

        private readonly LocalRedisFixture _redisFixture;

        public ContentMetadataEventStreamTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(TestGlobal.Logger, output)
        {
            Contract.RequiresNotNull(redis);
            _redisFixture = redis;
        }

        [Fact]
        public Task WriteSingleEvent()
        {
            return RunTest(async (context, stream, _, _) =>
            {
                var ev1 = new PutBlobRequest();
                await stream.WriteEventAsync(context, ev1).SelectResult(r => r.Should().BeTrue());
                var cursor = await stream.BeforeCheckpointAsync(context).ThrowIfFailureAsync();

                var events = await CollectStream(context, stream, cursor).ThrowIfFailureAsync();
                events.Count.Should().Be(1);
                events[0].Should().BeEquivalentTo(ev1);
            });
        }

        [Fact]
        public Task GarbageCollectSingleEvent()
        {
            return RunTest(async (context, stream, _, _) =>
            {
                var ev1 = new PutBlobRequest();
                await stream.WriteEventAsync(context, ev1).SelectResult(r => r.Should().BeTrue());

                var cursor = await stream.BeforeCheckpointAsync(context).ThrowIfFailureAsync();
                await stream.AfterCheckpointAsync(context, cursor).ShouldBeSuccess();

                var requests = await CollectStream(context, stream, cursor).ThrowIfFailureAsync();
                requests.Count.Should().Be(0);
            });
        }

        [Theory]
        [InlineData(FailureMode.None, FailureMode.None)]
        [InlineData(FailureMode.None, FailureMode.All)]
        public Task WriteAndReadManyEventsSingleBlock(FailureMode persistentStorageFailure, FailureMode volatileStorageFailure)
        {
            var testSize = 100;

            return RunTest(async (context, stream, _, _) =>
            {
                var requests = Enumerable.Range(0, testSize).Select(idx => new RegisterContentLocationsRequest()
                {
                    MachineId = new MachineId(idx),
                }).ToArray();

                // Wait one after the other to ensure the sequencing is exactly what we expect
                foreach (var request in requests)
                {
                    await stream.WriteEventAsync(context, request).SelectResult(r => r.Should().BeTrue());
                }

                var cursor = await stream.BeforeCheckpointAsync(context).ThrowIfFailureAsync();

                if (volatileStorageFailure == FailureMode.All)
                {
                    await CollectStream(context, stream, cursor).ShouldBeError();
                }
                else
                {
                    var events = await CollectStream(context, stream, cursor).ThrowIfFailureAsync();
                    events.Count.Should().Be(testSize);

                    for (var i = 0; i < requests.Length; i++)
                    {
                        (events[i] as RegisterContentLocationsRequest).MachineId.Should().BeEquivalentTo(requests[i].MachineId);
                    }
                }
            },
            persistentStorageFailure: persistentStorageFailure,
            volatileStorageFailure: volatileStorageFailure);
        }

        [Theory]
        [InlineData(FailureMode.None, FailureMode.None)]
        [InlineData(FailureMode.None, FailureMode.All)]
        public Task WriteAndReadManyEventsAcrossBlocks(FailureMode persistentStorageFailure, FailureMode volatileStorageFailure)
        {
            var testSize = 5;
            var logRefreshFrequency = TimeSpan.FromMinutes(5);

            return RunTest(async (context, stream, _, _) =>
            {
                var requests = Enumerable.Range(0, testSize).Select(idx => new RegisterContentLocationsRequest()
                {
                    MachineId = new MachineId(idx),
                }).ToArray();

                // Wait one after the other to ensure the sequencing is exactly what we expect
                foreach (var request in requests)
                {
                    await stream.WriteEventAsync(context, request).SelectResult(r => r.Should().BeTrue());
                    await stream.WriteBehindCommitLoopIterationAsync(context);
                }

                var cursor = await stream.BeforeCheckpointAsync(context).ThrowIfFailureAsync();

                if (volatileStorageFailure == FailureMode.All)
                {
                    await CollectStream(context, stream, cursor).ShouldBeError();
                }
                else
                {
                    var events = await CollectStream(context, stream, cursor).ThrowIfFailureAsync();
                    events.Count.Should().Be(testSize);
                }
            }, contentMetadataEventStreamConfiguration: new ContentMetadataEventStreamConfiguration()
            {
                LogBlockRefreshInterval = logRefreshFrequency,
            },
            persistentStorageFailure: persistentStorageFailure,
            volatileStorageFailure: volatileStorageFailure);
        }

        [Theory]
        [InlineData(FailureMode.None, FailureMode.None)]
        [InlineData(FailureMode.None, FailureMode.All)]
        public Task WriteAndReadManyEventsAcrossCheckpoints(FailureMode persistentStorageFailure, FailureMode volatileStorageFailure)
        {
            var testSize = 5;

            return RunTest(async (context, stream, _, _) =>
            {
                var requests = Enumerable.Range(0, testSize).Select(idx => new RegisterContentLocationsRequest()
                {
                    MachineId = new MachineId(idx),
                }).ToArray();

                var cursors = new CheckpointLogId[testSize];
                foreach (var indexed in requests.AsIndexed())
                {
                    await stream.WriteEventAsync(context, indexed.Item).SelectResult(r => r.Should().BeTrue());
                    cursors[indexed.Index] = await stream.BeforeCheckpointAsync(context).ThrowIfFailureAsync();

                    // By not calling AfterCheckpointAsync, we stop GC from happening. This should let us read
                    // all events we have posted
                }

                if (volatileStorageFailure == FailureMode.All)
                {
                    await CollectStream(context, stream, cursors[0]).ShouldBeError();
                }
                else
                {
                    var events = await CollectStream(context, stream, cursors[0]).ThrowIfFailureAsync();
                    events.Count.Should().Be(testSize);
                }
            },
            persistentStorageFailure: persistentStorageFailure,
            volatileStorageFailure: volatileStorageFailure);
        }

        [Theory]
        [InlineData(FailureMode.None, FailureMode.None)]
        [InlineData(FailureMode.None, FailureMode.All)]
        public Task WriteAndReadManyEventsAcrossCheckpointsAndBlocks(FailureMode persistentStorageFailure, FailureMode volatileStorageFailure)
        {
            var logRefreshFrequency = TimeSpan.FromMinutes(5);
            var testSize = 5;

            return RunTest(async (context, stream, _, _) =>
            {
                var requests = Enumerable.Range(0, testSize).Select(idx => new RegisterContentLocationsRequest()
                {
                    MachineId = new MachineId(idx),
                }).ToArray();

                var cursors = new CheckpointLogId[testSize];
                foreach (var indexed in requests.AsIndexed())
                {
                    await stream.WriteEventAsync(context, indexed.Item).SelectResult(r => r.Should().BeTrue());

                    await stream.WriteBehindCommitLoopIterationAsync(context);

                    await stream.WriteEventAsync(context, indexed.Item).SelectResult(r => r.Should().BeTrue());

                    cursors[indexed.Index] = await stream.BeforeCheckpointAsync(context).ThrowIfFailureAsync();

                    // By not calling AfterCheckpointAsync, we stop GC from happening. This should let us read
                    // all events we have posted
                }

                if (volatileStorageFailure == FailureMode.All)
                {
                    await CollectStream(context, stream, cursors[0]).ShouldBeError();
                }
                else
                {
                    var events = await CollectStream(context, stream, cursors[0]).ThrowIfFailureAsync();
                    events.Count.Should().Be(2 * testSize);
                }
            }, contentMetadataEventStreamConfiguration: new ContentMetadataEventStreamConfiguration()
            {
                LogBlockRefreshInterval = logRefreshFrequency,
            },
            persistentStorageFailure: persistentStorageFailure,
            volatileStorageFailure: volatileStorageFailure);
        }

        private Task<Result<List<ServiceRequestBase>>> CollectStream(OperationContext context, ContentMetadataEventStream stream, CheckpointLogId cursor)
        {
            var msg = $"Cursor=[{cursor}]";
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var events = new List<ServiceRequestBase>();

                await stream.ReadEventsAsync(context, cursor, r =>
                {
                    events.Add(r);
                    return new ValueTask(Task.CompletedTask);
                }).ThrowIfFailureAsync();

                return Result.Success(events);
            }, extraStartMessage: msg, extraEndMessage: _ => msg);
        }

        private async Task RunTest(
            Func<OperationContext, ContentMetadataEventStream, IFailureController, IFailureController, Task> runTestAsync,
            ContentMetadataEventStreamConfiguration contentMetadataEventStreamConfiguration = null,
            RedisVolatileEventStorageConfiguration redisVolatileEventLogConfiguration = null,
            FailureMode persistentStorageFailure = FailureMode.None,
            FailureMode volatileStorageFailure = FailureMode.None)
        {
            var tracingContext = new Context(Logger);
            var operationContext = new OperationContext(tracingContext);

            redisVolatileEventLogConfiguration ??= new RedisVolatileEventStorageConfiguration();
            using var database = LocalRedisProcessDatabase.CreateAndStartEmpty(_redisFixture, TestGlobal.Logger, SystemClock.Instance);
            var primaryFactory = await RedisDatabaseFactory.CreateAsync(
                operationContext,
                new LiteralConnectionStringProvider(database.ConnectionString),
                new RedisConnectionMultiplexerConfiguration() { LoggingSeverity = Severity.Error });
            var primaryDatabaseAdapter = new RedisDatabaseAdapter(primaryFactory, "keyspace");
            var redisVolatileEventStorage = new RedisWriteAheadEventStorage(redisVolatileEventLogConfiguration, primaryDatabaseAdapter);

            var mockPersistentEventStorage = new MockPersistentEventStorage();

            var volatileEventStorage = new FailingVolatileEventStorage(volatileStorageFailure, redisVolatileEventStorage);
            var persistentEventStorage = new FailingPersistentEventStorage(persistentStorageFailure, mockPersistentEventStorage);

            contentMetadataEventStreamConfiguration ??= new ContentMetadataEventStreamConfiguration();
            var contentMetadataEventStream = new ContentMetadataEventStream(contentMetadataEventStreamConfiguration, volatileEventStorage, persistentEventStorage);

            await contentMetadataEventStream.StartupAsync(operationContext).ThrowIfFailure();
            await contentMetadataEventStream.CompleteOrChangeLogAsync(operationContext, CheckpointLogId.InitialLogId);
            contentMetadataEventStream.SetIsLogging(true);
            await runTestAsync(operationContext, contentMetadataEventStream, volatileEventStorage, persistentEventStorage);
            await contentMetadataEventStream.ShutdownAsync(operationContext).ThrowIfFailure();
        }

        public override void Dispose()
        {
            _redisFixture.Dispose();
        }
    }

    public enum FailureMode
    {
        None = 0,
        Read = 1 << 0,
        Write = 1 << 1,
        SilentWrite = 1 << 2,
        All = 1 << 20 | Read | Write
    }

    public interface IFailureController
    {
        FailureMode FailureMode { get; set; }
    }

    public class FailingVolatileEventStorage : StartupShutdownSlimBase, IWriteAheadEventStorage, IFailureController
    {
        public IWriteAheadEventStorage InnerStorage { get; set; }
        public FailureMode FailureMode { get; set; }

        protected override Tracer Tracer { get; } = new Tracer(nameof(FailingVolatileEventStorage));

        public FailingVolatileEventStorage(FailureMode failureMode = FailureMode.All, IWriteAheadEventStorage innerStorage = null)
        {
            FailureMode = failureMode;
            InnerStorage = innerStorage;
        }

        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            if (InnerStorage != null)
            {
                return InnerStorage.StartupAsync(context);
            }

            return BoolResult.SuccessTask;
        }

        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            if (InnerStorage != null)
            {
                return InnerStorage.ShutdownAsync(context);
            }

            return BoolResult.SuccessTask;
        }

        public Task<BoolResult> AppendAsync(OperationContext context, BlockReference cursor, ReadOnlyMemory<byte> piece)
        {
            if (FailureMode.HasFlag(FailureMode.SilentWrite))
            {
                return BoolResult.SuccessTask;
            }
            else if (FailureMode.HasFlag(FailureMode.Write))
            {
                return Task.FromResult(new BoolResult("Volatile event storage failure"));
            }

            return InnerStorage.AppendAsync(context, cursor, piece);
        }

        public Task<Result<Optional<ReadOnlyMemory<byte>>>> ReadAsync(OperationContext context, BlockReference cursor)
        {
            if (FailureMode.HasFlag(FailureMode.Read))
            {
                return Task.FromResult(Result.FromErrorMessage<Optional<ReadOnlyMemory<byte>>>("Volatile event storage failure"));
            }

            return InnerStorage.ReadAsync(context, cursor);
        }

        public Task<BoolResult> GarbageCollectAsync(OperationContext context, BlockReference cursor)
        {
            if (FailureMode.HasFlag(FailureMode.SilentWrite))
            {
                return BoolResult.SuccessTask;
            }
            else if (FailureMode.HasFlag(FailureMode.Write))
            {
                return Task.FromResult(new BoolResult("Volatile event storage failure"));
            }

            return InnerStorage.GarbageCollectAsync(context, cursor);
        }
    }

    public class FailingPersistentEventStorage : StartupShutdownSlimBase, IWriteBehindEventStorage, IFailureController
    {
        public IWriteBehindEventStorage InnerStorage { get; set; }
        public FailureMode FailureMode { get; set; }

        protected override Tracer Tracer { get; } = new Tracer(nameof(FailingPersistentEventStorage));

        public FailingPersistentEventStorage(FailureMode failureMode = FailureMode.All, IWriteBehindEventStorage innerStorage = null)
        {
            FailureMode = failureMode;
            InnerStorage = innerStorage;
        }

        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            if (InnerStorage != null)
            {
                return InnerStorage.StartupAsync(context);
            }

            return BoolResult.SuccessTask;
        }

        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            if (InnerStorage != null)
            {
                return InnerStorage.ShutdownAsync(context);
            }

            return BoolResult.SuccessTask;
        }

        public Task<BoolResult> AppendAsync(OperationContext context, BlockReference cursor, Stream stream)
        {
            if (FailureMode.HasFlag(FailureMode.SilentWrite))
            {
                return BoolResult.SuccessTask;
            }
            else if (FailureMode.HasFlag(FailureMode.Write))
            {
                return Task.FromResult(new BoolResult("Persistent event storage failure"));
            }

            return InnerStorage.AppendAsync(context, cursor, stream);
        }

        public Task<Result<Optional<Stream>>> ReadAsync(OperationContext context, CheckpointLogId logId)
        {
            if (FailureMode.HasFlag(FailureMode.Read))
            {
                return Task.FromResult(Result.FromErrorMessage<Optional<Stream>>("Persistent event storage failure"));
            }

            return InnerStorage.ReadAsync(context, logId);
        }

        public Task<Result<bool>> IsSealedAsync(OperationContext context, CheckpointLogId logId)
        {
            if (FailureMode.HasFlag(FailureMode.Read))
            {
                return Task.FromResult(Result.FromErrorMessage<bool>("Persistent event storage failure"));
            }

            return InnerStorage.IsSealedAsync(context, logId);
        }

        public Task<BoolResult> SealAsync(OperationContext context, CheckpointLogId logId)
        {
            if (FailureMode.HasFlag(FailureMode.SilentWrite))
            {
                return BoolResult.SuccessTask;
            }
            else if (FailureMode.HasFlag(FailureMode.Write))
            {
                return Task.FromResult(new BoolResult(errorMessage: "Persistent event storage failure"));
            }

            return InnerStorage.SealAsync(context, logId);
        }

        public Task<BoolResult> GarbageCollectAsync(OperationContext context, CheckpointLogId logId)
        {
            if (FailureMode.HasFlag(FailureMode.SilentWrite))
            {
                return BoolResult.SuccessTask;
            }
            else if (FailureMode.HasFlag(FailureMode.Write))
            {
                return Task.FromResult(new BoolResult(errorMessage: "Persistent event storage failure"));
            }

            return InnerStorage.GarbageCollectAsync(context, logId);
        }
    }

    public class MockPersistentEventStorageState
    {
        public ConcurrentDictionary<int, MemoryStream> Blobs { get; } = new ConcurrentDictionary<int, MemoryStream>();
        public ConcurrentDictionary<int, bool> Seals { get; } = new ConcurrentDictionary<int, bool>();
    }

    public class MockPersistentEventStorage : StartupShutdownSlimBase, IWriteBehindEventStorage
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(MockPersistentEventStorage));

        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1);

        private MockPersistentEventStorageState State { get; }

        public MockPersistentEventStorage(MockPersistentEventStorageState state = null)
        {
            State = state ?? new MockPersistentEventStorageState();
        }

        public Task<BoolResult> AppendAsync(OperationContext context, BlockReference cursor, Stream stream)
        {
            var msg = $"{cursor}";
            return context.PerformOperationAsync(Tracer, async () =>
            {
                using var _ = await _lock.AcquireAsync(context.Token);

                Contract.Assert(!State.Seals.GetOrDefault(cursor.LogId.Value, false), $"Can't append to `{cursor}` because the log is sealed");

                State.Seals[cursor.LogId.Value] = false;
                var storage = State.Blobs.GetOrAdd(cursor.LogId.Value, _ => new MemoryStream());

                await stream.CopyToAsync(storage, 1024, context.Token);

                return BoolResult.Success;
            },
            extraStartMessage: msg,
            extraEndMessage: _ => msg);
        }

        public Task<BoolResult> GarbageCollectAsync(OperationContext context, CheckpointLogId acknowledgedLogId)
        {
            var msg = $"LogId=[{acknowledgedLogId}]";
            return context.PerformOperationAsync(Tracer, (Func<Task<BoolResult>>)(async () =>
            {
                using var exiter = await _lock.AcquireAsync(context.Token);

                foreach (var key in State.Blobs.Keys)
                {
                    Contract.Assert(State.Seals.ContainsKey(key), $"Log `{key}` exists but does not have a corresponding seal entry");

                    var currentLogId = new CheckpointLogId(key);
                    if (acknowledgedLogId.CompareTo(currentLogId) > 0)
                    {
                        State.Blobs.TryRemove(key, out _);
                        State.Seals.TryRemove(key, out _);
                    }
                }

                return BoolResult.Success;
            }),
            extraStartMessage: msg,
            extraEndMessage: _ => msg);
        }

        public Task<Result<bool>> IsSealedAsync(OperationContext context, CheckpointLogId logId)
        {
            var msg = $"LogId=[{logId}]";
            return context.PerformOperationAsync(Tracer, async () =>
            {
                using var _ = await _lock.AcquireAsync(context.Token);

                if (State.Seals.TryGetValue(logId.Value, out var seal))
                {
                    return Result.Success(seal);
                }

                return Result.Success(false);
            },
            extraStartMessage: msg,
            extraEndMessage: _ => msg);
        }

        public Task<Result<Optional<Stream>>> ReadAsync(OperationContext context, CheckpointLogId logId)
        {
            var msg = $"LogId=[{logId}]";
            return context.PerformOperationAsync(Tracer, async () =>
            {
                using var _ = await _lock.AcquireAsync(context.Token);

                if (!State.Blobs.TryGetValue(logId.Value, out var stream))
                {
                    return Result.Success(Optional<Stream>.Empty);
                }

                var resultStream = new MemoryStream(capacity: (int)stream.Length);
                stream.Position = 0;
                await stream.CopyToAsync(resultStream, 1024, context.Token);
                resultStream.Seek(0, SeekOrigin.Begin);

                return Result.Success<Optional<Stream>>(resultStream);
            },
            extraStartMessage: msg,
            extraEndMessage: _ => msg);
        }

        public Task<BoolResult> SealAsync(OperationContext context, CheckpointLogId logId)
        {

            var msg = $"LogId=[{logId}]";
            return context.PerformOperationAsync(Tracer, async () =>
            {
                using var _ = await _lock.AcquireAsync(context.Token);

                Contract.Assert(State.Blobs.ContainsKey(logId.Value), $"Can't seal non-existent log `{logId}`");
                State.Seals[logId.Value] = true;

                return BoolResult.Success;
            },
            extraStartMessage: msg,
            extraEndMessage: _ => msg);
        }
    }
}
