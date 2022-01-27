// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Configuration for <see cref="ContentMetadataEventStream"/>
    /// </summary>
    public record ContentMetadataEventStreamConfiguration
    {
        public TimeSpan LogBlockRefreshInterval { get; set; } = TimeSpan.FromSeconds(1);

        public bool BatchWriteAheadWrites { get; set; } = true;

        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(120);
    }

    /// <summary>
    /// Manages writing events to log stream. First commits events to a write ahead
    ///
    /// WriteBehind layout:
    ///     logId.bin -> Log
    ///     logId.seal -> [maybe some metadata about log?] (indicates log was fully written)
    /// WriteAhead layout:
    ///     logId|blockId -> [Event]*
    ///
    /// Formats:
    /// Log = [Block]*
    /// Block = [BlockId:int32][BlockByteLength:int32][EventCount:int32][Event]*
    /// </summary>
    public partial class ContentMetadataEventStream : StartupShutdownComponentBase
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(ContentMetadataEventStream));

        private readonly ContentMetadataEventStreamConfiguration _configuration;

        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

        private readonly IWriteAheadEventStorage _writeAheadEventStorage;
        private readonly IWriteBehindEventStorage _writeBehindEventStorage;

        private readonly ObjectPool<LogEvent> _eventPool = new ObjectPool<LogEvent>(() => new LogEvent(), e => e.Reset());

        private readonly SemaphoreSlim _writeBehindCommitLock = TaskUtilities.CreateMutex();
        private readonly ReaderWriterLockSlim _blockSwitchLock = new ReaderWriterLockSlim();
        private LogBlock _currentBlock = new LogBlock();
        private LogBlock _nextBlock = new LogBlock();
        private bool _isLogging;

        private readonly Stream _emptyStream = new MemoryStream();

        public ContentMetadataEventStream(
            ContentMetadataEventStreamConfiguration configuration,
            IWriteAheadEventStorage volatileStorage,
            IWriteBehindEventStorage persistentStorage)
        {
            _configuration = configuration;
            _writeAheadEventStorage = volatileStorage;
            _writeBehindEventStorage = persistentStorage;

            LinkLifetime(_writeBehindEventStorage);
            LinkLifetime(_writeAheadEventStorage);

            // Start the write behind commit loop which commits log events to write behind
            // storage at a specified interval
            RunInBackground(nameof(WriteBehindCommitLoopAsync), WriteBehindCommitLoopAsync, fireAndForget: true);
        }

        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            // Stop logging
            SetIsLogging(false);
            return base.ShutdownCoreAsync(context);
        }

        public Task<Result<CheckpointLogId>> BeforeCheckpointAsync(OperationContext context)
        {
            return context.PerformOperationAsync(Tracer, async () => Result.Success(await CompleteOrChangeLogAsync(context, moveToNextLog: true)));
        }

        public Task<BoolResult> AfterCheckpointAsync(OperationContext context, CheckpointLogId logId)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var nextLogId = new CheckpointLogId(logId.Value + 1);
                await _writeBehindEventStorage.GarbageCollectAsync(context, nextLogId).ThrowIfFailure();

                await _writeAheadEventStorage.GarbageCollectAsync(context, new BlockReference()
                {
                    LogId = nextLogId,
                }).IgnoreFailure();

                return BoolResult.Success;
            });
        }

        public void SetIsLogging(bool isLogging)
        {
            using (_blockSwitchLock.AcquireWriteLock())
            {
                _isLogging = isLogging;
            }
        }

        /// <summary>
        /// Reads the log events from the write behind store and write ahead store (if log is not sealed in write behind)
        /// </summary>
        public Task<Result<CheckpointLogId>> ReadEventsAsync(OperationContext context, CheckpointLogId logId, Func<ServiceRequestBase, ValueTask> handleEvent)
        {
            int blockId = -1;
            int eventCount = 0;
            long totalBytes = 0;
            int lastWriteBehindBlock = -1;

            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    while (true)
                    {
                        int operationReadEvents = 0;
                        long bytes = 0;
                        lastWriteBehindBlock = -1;
                        int foundBlocks = 0;
                        bool hasWriteBehindLog = false;
                        bool isSealed = false;

                        // Read the log events from write behind store
                        await context.PerformOperationAsync(
                            Tracer,
                            async () =>
                            {
                                isSealed = await _writeBehindEventStorage.IsSealedAsync(context, logId).ThrowIfFailureAsync();

                                var writeBehindResult = await _writeBehindEventStorage.ReadAsync(context, logId).ThrowIfFailureAsync();
                                if (writeBehindResult.HasValue)
                                {
                                    hasWriteBehindLog = true;
                                    using var writeBehindStream = writeBehindResult.Value;
                                    using var writeBehindReader = BuildXLReader.Create(writeBehindStream);

                                    bytes = writeBehindStream.Length;
                                    totalBytes += bytes;
                                    while (writeBehindStream.Position != writeBehindStream.Length)
                                    {
                                        foundBlocks++;
                                        foreach (var item in LogBlock.ReadBlockEventsWithHeader(context, writeBehindReader, out blockId))
                                        {
                                            operationReadEvents++;
                                            eventCount++;
                                            await handleEvent(item);
                                        }
                                    }

                                    lastWriteBehindBlock = blockId;
                                }

                                return BoolResult.Success;
                            },
                            caller: "ReadWriteBehindEventsAsync",
                            extraEndMessage: _ => $"IsSealed=[{isSealed}], LogId=[{logId}], BlockId=[{blockId}], Bytes=[{bytes}] Events=[{operationReadEvents}]").ThrowIfFailureAsync();

                        if (!isSealed && hasWriteBehindLog)
                        {
                            // The log isn't sealed in the writeBehind storage. Writer likely crashed and didn't get to seal,
                            // so we need to recover log entries for confirmed transactions from Redis
                            bool moveNext = true;
                            while (moveNext)
                            {
                                moveNext = await context.PerformOperationAsync(
                                    Tracer,
                                    async () =>
                                    {
                                        operationReadEvents = 0;
                                        blockId++;
                                        bytes = 0;
                                        var writeAheadResult = await _writeAheadEventStorage.ReadAsync(context, (logId, blockId)).ThrowIfFailureAsync();
                                        if (writeAheadResult.TryGetValue(out var readBlock))
                                        {
                                            foundBlocks++;
                                            using var writeAheadStream = new MemoryStream(readBlock.ToArray());
                                            bytes = writeAheadStream.Length;
                                            totalBytes += bytes;
                                            using var writeAheadReader = BuildXLReader.Create(writeAheadStream);
                                            foreach (var item in LogBlock.ReadBlockEvents(context, writeAheadReader, readBlock.Length))
                                            {
                                                operationReadEvents++;
                                                eventCount++;
                                                await handleEvent(item);
                                            }
                                        }
                                        else
                                        {
                                            return Result.Success(false);
                                        }

                                        return Result.Success(true);
                                    },
                                    caller: "ReadWriteAheadEventsAsync",
                                    extraEndMessage: _ => $"LogId=[{logId}], BlockId=[{blockId}], Bytes=[{bytes}], Events=[{operationReadEvents}]").ThrowIfFailureAsync();
                            }
                        }

                        if (!isSealed && foundBlocks == 0)
                        {
                            break;
                        }

                        logId = logId.Next();
                    }

                    return Result.Success(logId);
                },
                extraEndMessage: r => $"LogId=[{r.GetValueOrDefault()}] LastWriteBehindBlock=[{lastWriteBehindBlock}], BlockId=[{blockId}], Bytes=[{totalBytes}], Events=[{eventCount}]");
        }

        public async Task<bool> WriteEventAsync(OperationContext context, ServiceRequestBase request)
        {
            using var wrapper = GetEvent(request, out var logEvent);

            if (!TryAddToBlock(logEvent, out var block))
            {
                return false;
            }

            request.BlockId = block.QualifiedBlockId;

            if (_configuration.BatchWriteAheadWrites)
            {
                await block.Events.Writer.WriteAsync(logEvent);
                await logEvent.Completion.Task;
            }
            else
            {
                await _writeAheadEventStorage.AppendAsync(context, block.QualifiedBlockId, logEvent.GetBytes())
                    .FireAndForgetErrorsAsync(context);
            }

            return true;
        }

        public Task<BoolResult> ClearAsync(OperationContext context)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var r1 = _writeAheadEventStorage.GarbageCollectAsync(context, BlockReference.MaxValue);
                var r2 = _writeBehindEventStorage.GarbageCollectAsync(context, CheckpointLogId.MaxValue);
                await Task.WhenAll(r1, r2);
                return (await r1) & (await r2);
            });
        }

        private Task<BoolResult> CommitWriteBehindAsync(OperationContext context, LogBlock block)
        {
            var msg = $"{block.QualifiedBlockId} EventCount={block.EventCount} Length={block.Length}";
            return context.PerformOperationAsync(
                Tracer,
                () =>
                {
                    var stream = block.PreparePersist();
                    return _writeBehindEventStorage.AppendAsync(context, block.QualifiedBlockId, stream);
                },
                extraStartMessage: msg,
                extraEndMessage: r => msg);
        }

        private async Task WriteAheadCommitLoopAsync(OperationContext context, LogBlock block)
        {
            await Task.Yield();

            using var cancelContext = context.WithCancellationToken(block.WriteAheadCommitCancellation.Token);
            context = cancelContext;

            LogEvent writeAheadCommit = new LogEvent();
            var reader = block.Events.Reader;
            while (await reader.WaitToReadAsync(context.Token))
            {
                block.PendingWriteAheadLogEvents.Clear();
                writeAheadCommit.Reset();

                while (reader.TryRead(out var logEvent))
                {
                    if (!logEvent.Completion.Task.IsCompleted)
                    {
                        block.PendingWriteAheadLogEvents.Add(logEvent);
                        logEvent.CopyTo(writeAheadCommit.Stream);
                    }
                }

                if (block.PendingWriteAheadLogEvents.Count == 0)
                {
                    continue;
                }

                await _writeAheadEventStorage.AppendAsync(context, block.QualifiedBlockId, writeAheadCommit.GetBytes())
                    .FireAndForgetErrorsAsync(context);

                foreach (var completion in block.PendingWriteAheadLogEvents)
                {
                    completion.Completion.TrySetResult(true);
                }
            }
        }

        private async Task<BoolResult> WriteBehindCommitLoopAsync(OperationContext context)
        {
            while (!context.Token.IsCancellationRequested)
            {
                await Task.Delay(_configuration.LogBlockRefreshInterval, context.Token);

                if (_currentBlock.EventCount == 0)
                {
                    continue;
                }

                await WriteBehindCommitLoopIterationAsync(new OperationContext(context, CancellationToken.None))
                    .FireAndForgetErrorsAsync(context);
            }

            return BoolResult.Success;
        }

        public async Task<(bool committed, LogCursor cursor)> WriteBehindCommitLoopIterationAsync(
            OperationContext context,
            CheckpointLogId? nextLogId = null,
            bool moveToNextLog = false)
        {
            Contract.Requires(!moveToNextLog || nextLogId == null);

            using (await _writeBehindCommitLock.AcquireAsync())
            {
                var block = _currentBlock;
                if (moveToNextLog)
                {
                    nextLogId = block.QualifiedBlockId.LogId.Next();
                }

                BlockReference nextBlockId = nextLogId != null
                    ? (nextLogId.Value, 0)
                    : (_currentBlock.QualifiedBlockId.LogId, _currentBlock.QualifiedBlockId.LogBlockId + 1);

                if (nextBlockId.LogBlockId == 0)
                {
                    // Create the new empty log
                    // We do this so we can distinguish case where there is no log from a crash before first block is written
                    await _writeBehindEventStorage.AppendAsync(context, nextBlockId, _emptyStream)
                        .FireAndForgetErrorsAsync(context);
                }

                await _nextBlock.WriteAheadCommitCompletion
                    .FireAndForgetErrorsAsync(context);

                using (_blockSwitchLock.AcquireWriteLock())
                {
                    _nextBlock.Reset(nextBlockId);

                    var tmp = _currentBlock;
                    _currentBlock = _nextBlock;
                    _nextBlock = tmp;
                }

                _currentBlock.WriteAheadCommitCompletion = WriteAheadCommitLoopAsync(context, _currentBlock)
                    .FireAndForgetErrorsAsync(context, operation: nameof(LogBlock.WriteAheadCommitCompletion));

                bool committed = false;

                if (block.IsInitialized)
                {
                    var commitResult = await CommitWriteBehindAsync(context, block);
                    block.WriteAheadCommitCancellation.Cancel();
                    committed = commitResult.Succeeded;
                }

                return (committed, new LogCursor()
                {
                    LogId = block.QualifiedBlockId.LogId,
                    LogBlockId = block.QualifiedBlockId.LogBlockId,
                    SequenceNumber = block.EventCount
                });
            }
        }

        public Task<CheckpointLogId> CompleteOrChangeLogAsync(OperationContext context, CheckpointLogId? newLogId = null, bool moveToNextLog = false)
        {
            var msg = $"LogId=[{newLogId?.ToString() ?? "Unspecified"}] MoveToNextLog=[{moveToNextLog}]";
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var (committed, cursor) = await WriteBehindCommitLoopIterationAsync(context, newLogId, moveToNextLog: moveToNextLog);

                    // If we are not starting a new log and last block was successfully committed, we can seal the log
                    if (newLogId == null && committed)
                    {
                        await _writeBehindEventStorage.SealAsync(context, cursor.LogId).FireAndForgetErrorsAsync(context);
                    }

                    return Result.Success(cursor.LogId);
                },
                extraStartMessage: msg,
                extraEndMessage: r => $"{msg} CompletedLogId=[{r.GetValueOrDefault()}]").ThrowIfFailureAsync();
        }

        private bool TryAddToBlock(LogEvent logEvent, out LogBlock block)
        {
            using (_blockSwitchLock.AcquireReadLock())
            {
                if (!_isLogging || !_currentBlock.IsInitialized)
                {
                    block = null;
                    return false;
                }

                block = _currentBlock;
                _currentBlock.Add(logEvent);
                Analysis.IgnoreResult(_currentBlock.WriteAheadCommitCancellation.Token.Register(() =>
                {
                    logEvent.Completion.TrySetResult(false);
                }));
                return true;
            }
        }

        private PooledObjectWrapper<LogEvent> GetEvent(ServiceRequestBase request, out LogEvent logEvent)
        {
            var wrapper = _eventPool.GetInstance();
            logEvent = wrapper.Instance;

            // Strip out ContextId when serializing, because it takes quite a bit of space
            var contextId = request.ContextId;
            request.ContextId = null;
            MetadataServiceSerializer.SerializeWithLengthPrefix(logEvent.Stream, request);
            request.ContextId = contextId;

            return wrapper;
        }

        private static ServiceRequestBase ReadEvent(Stream stream)
        {
            return MetadataServiceSerializer.DeserializeWithLengthPrefix<ServiceRequestBase>(stream);
        }

        private class LogBlock
        {
            public CancellationTokenSource WriteAheadCommitCancellation { get; private set; } = new CancellationTokenSource();
            public Task WriteAheadCommitCompletion { get; set; } = Task.CompletedTask;
            public List<LogEvent> PendingWriteAheadLogEvents { get; } = new List<LogEvent>();

            public Channel<LogEvent> Events { get; } = Channel.CreateUnbounded<LogEvent>();

            private readonly ReaderWriterLockSlim _streamLock = new ReaderWriterLockSlim();

            public bool IsInitialized { get; private set; }
            public BlockReference QualifiedBlockId { get; private set; }
            private int _placeholderStartPosition;
            private int _eventsStartPosition;

            public int Length;
            public int EventCount;

            private byte[] _bytes = new byte[4096];

            public void Add(LogEvent logEvent)
            {
                var bytes = logEvent.GetBytes();
                int end = Interlocked.Add(ref Length, bytes.Length) + _eventsStartPosition;
                Interlocked.Increment(ref EventCount);

                if (end > _bytes.Length)
                {
                    // Take lock in order to expand stream
                    using (_streamLock.AcquireWriteLock())
                    {
                        if (end > _bytes.Length)
                        {
                            Array.Resize(ref _bytes, _eventsStartPosition + (Length * 2));
                        }
                    }
                }

                using (_streamLock.AcquireReadLock())
                {
                    bytes.CopyTo(_bytes.AsMemory(start: end - bytes.Length, bytes.Length));
                }
            }

            public MemoryStream PreparePersist()
            {
                var stream = new MemoryStream(_bytes, 0, _eventsStartPosition + Length);
                var writer = new BinaryWriter(stream);
                stream.Position = _placeholderStartPosition;

                // Write block byte length over placeholder
                writer.Write(Length);

                // Write block piece count over placeholder
                writer.Write(EventCount);

                stream.Position = 0;

                return stream;
            }

            public void Reset(BlockReference qualifiedBlockId)
            {
                IsInitialized = true;
                EventCount = 0;
                Length = 0;

                PendingWriteAheadLogEvents.Clear();
                WriteAheadCommitCancellation = new CancellationTokenSource();

                QualifiedBlockId = qualifiedBlockId;

                var stream = new MemoryStream(_bytes);
                var writer = new BinaryWriter(stream);

                stream.SetLength(0);

                // Write block id
                writer.Write(QualifiedBlockId.LogBlockId);

                _placeholderStartPosition = (int)stream.Position;

                // Write block byte length placeholder
                writer.Write((int)0);

                // Write block event count placeholder
                writer.Write((int)0);

                _eventsStartPosition = (int)stream.Position;
            }

            public static IEnumerable<ServiceRequestBase> ReadBlockEventsWithHeader(OperationContext context, BuildXLReader reader, out int blockId)
            {
                blockId = reader.ReadInt32();
                var blockByteLength = reader.ReadInt32();
                var blockEventCount = reader.ReadInt32();

                return ReadBlockEvents(context, reader, blockByteLength, blockEventCount);
            }

            public static IEnumerable<ServiceRequestBase> ReadBlockEvents(OperationContext context, BuildXLReader reader, int blockEventByteLength, int? blockEventCount = null)
            {
                int readEvents = 0;
                long start = reader.BaseStream.Position;
                while ((reader.BaseStream.Position - start) < blockEventByteLength)
                {
                    yield return ReadEvent(reader.BaseStream);
                    readEvents++;
                }

                if (blockEventCount != null && readEvents != blockEventCount)
                {
                    // TODO: Log error
                }
            }
        }

        private class LogEvent
        {
            public MemoryStream Stream { get; } = new MemoryStream();

            public TaskSourceSlim<bool> Completion { get; set; } = TaskSourceSlim.Create<bool>();

            public ReadOnlyMemory<byte> GetBytes()
            {
                var buffer = Stream.GetBuffer();
                return buffer.AsMemory(0, (int)Stream.Position);
            }

            public void Reset()
            {
                Stream.SetLength(0);
                Completion = TaskSourceSlim.Create<bool>();
            }

            internal void CopyTo(MemoryStream stream)
            {
                var buffer = Stream.GetBuffer();
                stream.Write(buffer, 0, (int)Stream.Position);
            }
        }
    }
}
