// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Configuration for <see cref="ContentMetadataEventStream"/>
    /// </summary>
    public record ContentMetadataEventStreamConfiguration
    {
        public TimeSpan LogBlockRefreshInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// The maximum frequency to write write-ahead events to storage
        /// </summary>
        public TimeSpan MinWriteAheadInterval { get; set; } = TimeSpan.Zero;

        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(120);
    }

    /// <summary>
    /// Manages writing events to a persistent log stream.
    ///
    /// WriteAhead layout:
    ///     logId|blockId -> [Event]*
    ///
    /// Formats:
    /// Log = [Block]*
    /// Block = [BlockId:int32][BlockByteLength:int32][EventCount:int32][Event]*
    /// </summary>
    public class ContentMetadataEventStream : StartupShutdownComponentBase, IContentMetadataEventStream
    {
        protected override Tracer Tracer { get; } = new(nameof(ContentMetadataEventStream));

        private readonly ContentMetadataEventStreamConfiguration _configuration;

        internal IWriteAheadEventStorage WriteAheadEventStorage { get; }

        private readonly ObjectPool<LogEvent> _eventPool = new(() => new LogEvent(), e => e.Reset());

        private readonly SemaphoreSlim _writeBehindCommitLock = TaskUtilities.CreateMutex();
        private readonly ReaderWriterLockSlim _blockSwitchLock = new();
        private LogBlock _currentBlock = new();
        private LogBlock _nextBlock = new();
        private bool _active;

        public ContentMetadataEventStream(
            ContentMetadataEventStreamConfiguration configuration,
            IWriteAheadEventStorage volatileStorage)
        {
            _configuration = configuration;
            WriteAheadEventStorage = volatileStorage;
            LinkLifetime(WriteAheadEventStorage);

            // Start the write behind commit loop which commits log events to write behind
            // storage at a specified interval
            RunInBackground(nameof(CommitLoopAsync), CommitLoopAsync, fireAndForget: true);
        }

        protected override async Task<BoolResult> ShutdownComponentAsync(OperationContext context)
        {
            // Stop logging
            Toggle(false);
            _currentBlock.Events.Writer.TryComplete();
            _nextBlock.Events.Writer.TryComplete();
            await _currentBlock.WriteAheadCommitCompletion;
            await _nextBlock.WriteAheadCommitCompletion;
            return BoolResult.Success;
        }

        public Task<Result<CheckpointLogId>> BeforeCheckpointAsync(OperationContext context)
        {
            // This function is essentially responsible for obtaining the highest persisted log ID and switching it to
            // the next one. It also returns the highest persisted log ID so it can be stored in the checkpoint for
            // later recovery in case of a crash.
            return context.PerformOperationAsync(Tracer, async () => Result.Success(await CompleteOrChangeLogAsync(context, moveToNextLog: true)));
        }

        public Task<BoolResult> AfterCheckpointAsync(OperationContext context, CheckpointLogId logId)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var nextLogId = new CheckpointLogId(logId.Value + 1);

                // We ignore the failure here because: (a) it's already logged, and (b) we don't want to fail the
                // caller because this method is called repeatedly. Failure doesn't affect system correctness other
                // than the fact that we might accumulate some garbage for a short period of time.
                await WriteAheadEventStorage.GarbageCollectAsync(context, new BlockReference { LogId = nextLogId, }).IgnoreFailure();

                return BoolResult.Success;
            });
        }

        public void Toggle(bool active)
        {
            using (_blockSwitchLock.AcquireWriteLock())
            {
                _active = active;
            }
        }

        /// <summary>
        /// Reads the log events
        /// </summary>
        public Task<Result<CheckpointLogId>> ReadEventsAsync(OperationContext context, CheckpointLogId logId, Func<ServiceRequestBase, ValueTask> handleEvent)
        {
            long blocks = 0;
            long events = 0;
            long bytes = 0;

            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    for (var hasMoreLogs = true; hasMoreLogs; logId = logId.Next())
                    {
                        bool hasMoreBlocks = true;
                        for (int blockId = 0; hasMoreBlocks; blockId++)
                        {
                            blocks++;

                            var reference = new BlockReference(logId, blockId);

                            // The call below can only fail when there's a non-retriable Azure storage issue, at which
                            // point we have no alternative other than to give up.
                            var result = await processBlock(context, reference).ThrowIfFailureAsync();
                            events += result.Events;
                            bytes += result.Length;

                            // We can only land here if either the data was successfully processed, or we had to skip
                            // some number of entries. The non-existence of a blob is the signal that we're done
                            // reading.
                            if (!result.Exists)
                            {
                                hasMoreBlocks = false;
                                if (blockId == 0)
                                {
                                    hasMoreLogs = false;
                                }
                            }
                        }
                    }

                    // The for loop above will increment the logId one last time when we have determined there are no
                    // more logs. The Prev below ensures we return the actual log ID we meant to return.
                    return Result.Success(logId.Prev());
                },
                extraEndMessage: r => $"LogId=[{r.GetValueOrDefault()}] Blocks=[{blocks}] Bytes=[{bytes}] Events=[{events}]");

            Task<Result<ProcessBlockResult>> processBlock(OperationContext context, BlockReference blockId)
            {
                var events = 0;
                var bytes = 0;

                return context.PerformOperationAsync(
                    Tracer,
                    async () =>
                    {
                        // It is important that this block remain outside of the try-catch below. The reason is that
                        // the following line can only fail if there's a problem talking with storage, and the problem
                        // is persistent.
                        // If we put it inside the try-catch, the method will return that the block exists, even though
                        // it might not. In doing so, it will cause a never-ending loop in ReadEventsAsync.
                        var writeAheadResult = await WriteAheadEventStorage.ReadAsync(context, blockId).ThrowIfFailureAsync();
                        if (!writeAheadResult.TryGetValue(out var readBlock))
                        {
                            return Result.Success(ProcessBlockResult.NonExisting);
                        }

                        try
                        {
                            bytes = readBlock.Length;

                            using var writeAheadStream = new MemoryStream(readBlock.ToArray());
                            using var writeAheadReader = BuildXLReader.Create(writeAheadStream);
                            foreach (var item in LogBlock.ReadBlockEvents(writeAheadReader, readBlock.Length))
                            {
                                try
                                {
                                    await handleEvent(item);
                                }
                                catch (Exception exception)
                                {
                                    var cursor = new LogCursor()
                                    {
                                        Block = blockId,
                                        SequenceNumber = events
                                    };
                                    Tracer.Error(context, exception, $"Failure while handling event. Event will be skipped. Cursor=[{cursor}]");
                                }

                                events++;
                            }

                            return Result.Success(new ProcessBlockResult(Exists: true, bytes, events));
                        }
                        catch (Exception exception)
                        {
                            // This catch is intentional and meant to prevent deserialization issues in a single block
                            // from preventing restore from completing successfully.
                            var cursor = new LogCursor()
                            {
                                Block = blockId,
                                SequenceNumber = events
                            };
                            Tracer.Error(context, exception, $"Failure while traversing events in block. Remaining events will be skipped. Cursor=[{cursor}]");
                            return Result.Success(new ProcessBlockResult(Exists: true, bytes, events));
                        }
                    },
                    caller: "ReadWriteAheadEventsAsync",
                    extraEndMessage: result =>
                                     {
                                         var extra = string.Empty;
                                         if (result.Succeeded)
                                         {
                                             extra = $" Length=[{result.Value?.Length ?? -1}] Events=[{result.Value?.Events ?? -1}]";
                                         }

                                         return $"{blockId}{extra}";
                                     });
            }
        }

        private record ProcessBlockResult(bool Exists, long Length, long Events)
        {
            public static readonly ProcessBlockResult NonExisting = new(Exists: false, 0, 0);
        }

        public async Task<bool> WriteEventAsync(OperationContext context, ServiceRequestBase request)
        {
            using var wrapper = GetEvent(request, out var logEvent);

            if (!TryAddToBlock(logEvent, out var block))
            {
                return false;
            }

            request.BlockId = block.QualifiedBlockId;

            await block.Events.Writer.WriteAsync(logEvent);
            await logEvent.WriteAheadWriteCompleted.Task;

            return true;
        }

        public Task<BoolResult> ClearAsync(OperationContext context)
        {
            return context.PerformOperationAsync(Tracer, () => WriteAheadEventStorage.GarbageCollectAsync(context, BlockReference.MaxValue));
        }

        private async Task WriteAheadCommitLoopAsync(OperationContext context, LogBlock block)
        {
            // We need to yield here to ensure we don't block the caller. This is important because the caller is
            // the same task that is responsible for triggering block changes.
            await Task.Yield();

            var sw = Stopwatch.StartNew();

            LogEvent writeAheadCommit = new LogEvent();
            var reader = block.Events.Reader;
            while (await reader.WaitToReadAsync())
            {
                block.PendingWriteAheadLogEvents.Clear();
                writeAheadCommit.Reset();

                while (reader.TryRead(out var logEvent))
                {
                    Contract.Assert(!logEvent.WriteAheadWriteCompleted.Task.IsCompleted);
                    block.PendingWriteAheadLogEvents.Add(logEvent);
                    logEvent.CopyTo(writeAheadCommit.Stream);
                }

                if (block.PendingWriteAheadLogEvents.Count == 0)
                {
                    continue;
                }

                // The following call is what actually persists the block. It will only ever fail if we have retried
                // enough that we're unwilling to retry further. If this fails, the error will be logged, and there's
                // really nothing we can do here other than move on to the next block.
                await WriteAheadEventStorage.AppendAsync(context, block.QualifiedBlockId, writeAheadCommit.GetBytes())
                    .FireAndForgetErrorsAsync(context);

                foreach (var completion in block.PendingWriteAheadLogEvents)
                {
                    completion.WriteAheadWriteCompleted.TrySetResult(true);
                }

                // The following code is meant to ensure that we don't write too often into storage.
                try
                {
                    var elapsedTimeSinceLastWrite = sw.Elapsed;
                    var waitTime = _configuration.MinWriteAheadInterval - elapsedTimeSinceLastWrite;
                    if (waitTime > TimeSpan.Zero)
                    {
                        await Task.Delay(waitTime, context.Token);
                    }
                }
                catch (Exception exception)
                {
                    Tracer.Error(context, exception, "Failure while waiting for next write ahead commit.");
                }

                sw.Restart();
            }
        }

        private async Task<BoolResult> CommitLoopAsync(OperationContext context)
        {
            while (!context.Token.IsCancellationRequested)
            {
                await Task.Delay(_configuration.LogBlockRefreshInterval, context.Token);

                if (_currentBlock.EventCount == 0)
                {
                    continue;
                }

                await CommitLoopIterationAsync(context)
                    .FireAndForgetErrorsAsync(context);
            }

            return BoolResult.Success;
        }

        /// <summary>
        /// This method is responsible for ensuring data is persisted in the write ahead log. It gets called from two
        /// places:
        ///  1. <see cref="CommitLoopAsync"/>
        ///  2. <see cref="CompleteOrChangeLogAsync"/>
        ///
        /// GCS will write events into a single log at any given time. Each log gets a log ID (a number). In order to
        /// allow us to write atomically, events are written in blocks, which also get an ID (a number); and within a
        /// block events are assigned a sequence number. <see cref="LogCursor"/> identifies a specific event as (log
        /// ID, block ID, sequence number).
        ///
        /// 
        /// </summary>
        public async Task<(bool committed, LogCursor cursor)> CommitLoopIterationAsync(
            OperationContext context,
            CheckpointLogId? nextLogId = null,
            bool moveToNextLog = false)
        {
            Contract.Requires(!moveToNextLog || nextLogId == null);

            // We don't want to allow cancellations to happen during a commit because it is a critical operation.
            context = context.WithoutCancellationToken();

            using (await _writeBehindCommitLock.AcquireAsync())
            {
                var block = _currentBlock;
                if (moveToNextLog)
                {
                    nextLogId = block.QualifiedBlockId.LogId.Next();
                }

                BlockReference nextBlockId = nextLogId?.FirstBlock() ?? _currentBlock.QualifiedBlockId.Next();

                // Ensure any errors that may have happened in the next block are logged into the current context
                await _nextBlock.WriteAheadCommitCompletion
                    .FireAndForgetErrorsAsync(context);

                // Create a new block and set it as active
                using (_blockSwitchLock.AcquireWriteLock())
                {
                    _nextBlock.Reset(nextBlockId);
                    (_currentBlock, _nextBlock) = (_nextBlock, _currentBlock);
                    block.Events.Writer.Complete();
                }

                // Start the background task that ensures the new block commits
                _currentBlock.WriteAheadCommitCompletion = WriteAheadCommitLoopAsync(context, _currentBlock)
                    .FireAndForgetErrorsAsync(context, operation: nameof(LogBlock.WriteAheadCommitCompletion));

                bool committed = false;

                if (block.IsInitialized)
                {
                    await block.WriteAheadCommitCompletion;
                    committed = true;
                }

                return (committed, new LogCursor()
                {
                    Block = block.QualifiedBlockId,
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
                    var (_, cursor) = await CommitLoopIterationAsync(context, newLogId, moveToNextLog: moveToNextLog);

                    return Result.Success(cursor.LogId);
                },
                extraStartMessage: msg,
                extraEndMessage: r => $"{msg} CompletedLogId=[{r.GetValueOrDefault()}]").ThrowIfFailureAsync();
        }

        private bool TryAddToBlock(LogEvent logEvent, out LogBlock block)
        {
            using (_blockSwitchLock.AcquireReadLock())
            {
                block = _currentBlock;

                if (!_active || !block.IsInitialized)
                {
                    block = null;
                    return false;
                }

                block.Add(logEvent);
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
            public Task WriteAheadCommitCompletion { get; set; } = Task.CompletedTask;
            public List<LogEvent> PendingWriteAheadLogEvents { get; } = new List<LogEvent>();

            public Channel<LogEvent> Events { get; private set; } = Channel.CreateUnbounded<LogEvent>();

            private readonly ReaderWriterLockSlim _streamLock = new ReaderWriterLockSlim();

            /// <summary>
            /// This field will be false when the instance is created and true as soon as it's first reset.
            /// </summary>
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

            public void Reset(BlockReference qualifiedBlockId)
            {
                IsInitialized = true;
                EventCount = 0;
                Length = 0;

                PendingWriteAheadLogEvents.Clear();
                QualifiedBlockId = qualifiedBlockId;
                Events = Channel.CreateUnbounded<LogEvent>();
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

            public static IEnumerable<ServiceRequestBase> ReadBlockEvents(BuildXLReader reader, int blockEventByteLength, int? blockEventCount = null)
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
            public MemoryStream Stream { get; } = new();

            public TaskSourceSlim<bool> WriteAheadWriteCompleted { get; private set; } = TaskSourceSlim.Create<bool>();

            public ReadOnlyMemory<byte> GetBytes()
            {
                var buffer = Stream.GetBuffer();
                return buffer.AsMemory(0, (int)Stream.Position);
            }

            public void Reset()
            {
                Stream.SetLength(0);
                WriteAheadWriteCompleted = TaskSourceSlim.Create<bool>();
            }

            internal void CopyTo(MemoryStream stream)
            {
                var buffer = Stream.GetBuffer();
                stream.Write(buffer, 0, (int)Stream.Position);
            }
        }
    }
}
