// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
using static BuildXL.Cache.ContentStore.Distributed.Tracing.TracingStructuredExtensions;
using static BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming.ContentLocationEventStoreCounters;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// Base event store implementation for processing events from local or remote event hub.
    /// </summary>
    public abstract class ContentLocationEventStore : StartupShutdownSlimBase
    {
        private readonly ContentLocationEventStoreConfiguration _configuration;

        /// <summary>
        /// Indicates the maximum amount of content which will be sent via events vs storage for reconciliation.
        /// If under threshold, the events are sent via standard event streaming pipeline
        /// If over threshold, the events are serialized to storage instead and a single event is sent with storage id.
        /// </summary>
        public const int LargeEventContentCountThreshold = 10000;

        /// <summary>
        /// Indicates the maximum amount of content which will be sent via events vs storage for update metadata entry.
        /// If under threshold, the events are sent via standard event streaming pipeline
        /// If over threshold, the events are serialized to storage instead and a single event is sent with storage id.
        /// </summary>
        public const int LargeUpdateMetadataEventHashCountThreshold = 5000;

        /// <inheritdoc />
        protected override Tracer Tracer { get; }

        /// <nodoc />
        protected readonly IContentLocationEventHandler EventHandler;

        /// <nodoc />
        public IClock Clock { get; }

        private readonly CentralStorage _storage;
        private readonly Interfaces.FileSystem.AbsolutePath _workingDirectory;
        
        private readonly IAbsFileSystem _fileSystem;
        private readonly DisposableDirectory _workingDisposableDirectory;

        /// <nodoc />
        protected readonly ContentLocationEventDataSerializer EventDataSerializer;

        /// <nodoc />
        public CounterCollection<ContentLocationEventStoreCounters> Counters { get; } = new CounterCollection<ContentLocationEventStoreCounters>();

        /// <summary>
        /// Nagle queue for all events send via event hub.
        /// </summary>
        protected NagleQueue<(OperationContext context, ContentLocationEventData data)>? EventNagleQueue;

        /// <inheritdoc />
        protected ContentLocationEventStore(
            ContentLocationEventStoreConfiguration configuration,
            string name,
            IContentLocationEventHandler eventHandler,
            CentralStorage centralStorage,
            Interfaces.FileSystem.AbsolutePath workingDirectory,
            IClock clock)
        {
            _configuration = configuration;
            _fileSystem = new PassThroughFileSystem();
            _storage = centralStorage;
            _workingDisposableDirectory = new DisposableDirectory(_fileSystem, workingDirectory);
            _workingDirectory = workingDirectory;
            EventHandler = eventHandler;
            Clock = clock;
            var tracer = new Tracer(name) { LogOperationStarted = false };
            Tracer = tracer;

            ValidationMode validationMode = configuration.SelfCheckSerialization ? (configuration.SelfCheckSerializationShouldFail ? ValidationMode.Fail : ValidationMode.Trace) : ValidationMode.Off;

            // EventDataSerializer is not thread-safe.
            // This is usually not a problem, because the nagle queue that is used by this class
            // kind of guarantees that it would be just a single thread responsible for sending the events
            // to event hub.
            // But this is not the case when the batch size is 1 (used by tests only).
            // In this case a special version of a nagle queue is created, that doesn't have this guarantee.
            // In this case this method can be called from multiple threads causing serialization/deserialization issues.
            // So to prevent random test failures because of the state corruption we're using lock
            // if the batch size is 1.
            EventDataSerializer = new ContentLocationEventDataSerializer(validationMode, synchronize: _configuration.EventBatchSize == 1);
        }

        /// <summary>
        /// Factory method for creating an instance of <see cref="ContentLocationEventStore"/> based on <paramref name="configuration"/>.
        /// </summary>
        public static ContentLocationEventStore Create(
            ContentLocationEventStoreConfiguration configuration,
            IContentLocationEventHandler eventHandler,
            string localMachineName,
            CentralStorage centralStorage,
            Interfaces.FileSystem.AbsolutePath workingDirectory,
            IClock clock)
        {
            Contract.RequiresNotNull(configuration);
            return new EventHubContentLocationEventStore(configuration, eventHandler, localMachineName, centralStorage, workingDirectory, clock);
        }

        /// <summary>
        /// Dispatch the <paramref name="eventData"/> to an event handler specified during instance construction.
        /// </summary>
        /// <remarks>
        /// The method used only by tests.
        /// </remarks>
        internal Task DispatchAsync(OperationContext context, ContentLocationEventData eventData)
        {
            return DispatchAsync(context, eventData, Counters, visitor: new UpdatedHashesVisitor());
        }

        /// <nodoc />
        protected async Task DispatchAsync(OperationContext context, ContentLocationEventData eventData, CounterCollection<ContentLocationEventStoreCounters> counters, UpdatedHashesVisitor visitor)
        {
            switch (eventData)
            {
                case AddContentLocationEventData addContent:
                    using (counters[DispatchAddLocations].Start())
                    {
                        counters[DispatchAddLocationsHashes].Add(eventData.ContentHashes.Count);
                        visitor?.AddLocationsHashProcessed(addContent.ContentHashes);

                        var stateChanges = EventHandler.LocationAdded(
                            context,
                            addContent.Sender,
                            addContent.ContentHashes.SelectList((hash, index) => new ShortHashWithSize(hash, addContent.ContentSizes[index])),
                            eventData.Reconciling,
                            updateLastAccessTime: addContent.Touch);
                        counters[DatabaseAddedLocations].Add(stateChanges);
                    }

                    break;
                case RemoveContentLocationEventData removeContent:
                    using (counters[DispatchRemoveLocations].Start())
                    {
                        counters[DispatchRemoveLocationsHashes].Add(eventData.ContentHashes.Count);
                        visitor?.RemoveLocationsHashProcessed(eventData.ContentHashes);

                        var stateChanges = EventHandler.LocationRemoved(context, removeContent.Sender, removeContent.ContentHashes, eventData.Reconciling);
                        counters[DatabaseRemovedLocations].Add(stateChanges);
                    }
                    break;
                case TouchContentLocationEventData touchContent:
                    using (counters[DispatchTouch].Start())
                    {
                        counters[DispatchTouchHashes].Add(eventData.ContentHashes.Count);
                        var stateChanges = EventHandler.ContentTouched(context, touchContent.Sender, touchContent.ContentHashes, touchContent.AccessTime);
                        counters[DatabaseTouchedLocations].Add(stateChanges);
                    }
                    break;
                case BlobContentLocationEventData blobEvent:
                    using (counters[DispatchBlob].Start())
                    {
                        await GetDeserializeAndDispatchBlobEventAsync(context, blobEvent, counters, visitor);
                    }
                    break;
                case UpdateMetadataEntryEventData updateMetadata:
                    using (counters[DispatchUpdateMetadata].Start())
                    {
                        var stateChanges = EventHandler.MetadataUpdated(context, updateMetadata.StrongFingerprint, updateMetadata.Entry);
                        counters[DatabaseUpdatedMetadata].Add(stateChanges);
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unknown ContentLocationEventData type '{eventData.GetType()}'.");
            }
        }

        private Task GetDeserializeAndDispatchBlobEventAsync(
            OperationContext context,
            BlobContentLocationEventData blobEvent,
            CounterCollection<ContentLocationEventStoreCounters> counters,
            UpdatedHashesVisitor visitor)
        {
            int batchSize = -1;
            TimeSpan? getAndDeserializedDuration = null;
            TimeSpan? dispatchBlobEventDataDuration = null;

            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    IReadOnlyList<ContentLocationEventData> eventDatas;

                    using (var timer = counters[GetAndDeserializeEventData].Start())
                    {
                        eventDatas = await getAndDeserializeLargeEventDataAsync();

                        getAndDeserializedDuration = timer.Elapsed;
                    }

                    using (var timer = counters[DispatchBlobEventData].Start())
                    {
                        batchSize = eventDatas.Count;
                        foreach (var eventData in eventDatas)
                        {
                            if (eventData.Kind == EventKind.AddLocation
                                || eventData.Kind == EventKind.AddLocationWithoutTouching
                                || eventData.Kind == EventKind.RemoveLocation)
                            {
                                // Add or remove events only go through this code path if reconciling
                                eventData.Reconciling = true;
                            }

                            await DispatchAsync(context, eventData, counters, visitor);
                        }

                        dispatchBlobEventDataDuration = timer.Elapsed;
                    }

                    return BoolResult.Success;
                },
                extraEndMessage: _ => $"BlobName={blobEvent.BlobId} Size=[{batchSize}] GetAndDeserializedDuration={getAndDeserializedDuration.GetValueOrDefault().TotalMilliseconds}ms DispatchBlobEventDuration={dispatchBlobEventDataDuration.GetValueOrDefault().TotalMilliseconds}ms")
                .ThrowIfFailure();

            async Task<IReadOnlyList<ContentLocationEventData>> getAndDeserializeLargeEventDataAsync()
            {
                var blobFilePath = _workingDirectory / Guid.NewGuid().ToString();
                var blobName = blobEvent.BlobId;

                await _storage.TryGetFileAsync(context, blobName, blobFilePath).ThrowIfFailure();

                using var stream = await _fileSystem.OpenSafeAsync(
                    blobFilePath,
                    FileAccess.Read,
                    FileMode.Open,
                    FileShare.Read | FileShare.Delete,
                    FileOptions.DeleteOnClose,
                    AbsFileSystemExtension.DefaultFileStreamBufferSize);
                using var reader = BuildXLReader.Create(stream, leaveOpen: true);

                // Calling ToList to force materialization of IEnumerable to avoid access of disposed stream.
                return EventDataSerializer.DeserializeEvents(reader).ToList();
            }
        }

        /// <nodoc />
        protected virtual void Publish(OperationContext context, ContentLocationEventData eventData)
        {
            EventNagleQueue?.Enqueue((context, eventData));
        }

        /// <nodoc />
        protected Task<BoolResult> SendEventsAsync(OperationContext context, ContentLocationEventData[] events)
        {
            Tracer.Info(context, $"{Tracer.Name}: Sending {events.Length} event(s) to event hub.");

            var counters = new CounterCollection<ContentLocationEventStoreCounters>();

            context = context.CreateNested(nameof(ContentLocationEventStore));
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var operations = events.SelectMany(
                        e =>
                        {
                            var operation = GetOperation(e);
                            var reason = e.Reconciling ? OperationReason.Reconcile : OperationReason.Unknown;
                            return e.ContentHashes.Select(hash => (hash, operation, reason));
                        }).ToList();
                    LogContentLocationOperations(context, Tracer.Name, operations);

                    // Using local counters instance for tracing purposes.
                    counters[SentEventsCount].Add(events.Length);

                    updateCountersWith(counters, events);

                    var result = await SendEventsCoreAsync(context, events, counters);

                    // Updating global counters based on the operation results.
                    Counters.Append(counters);

                    if (result)
                    {
                        // Trace successful case separately.
                        context.LogSendEventsOverview(counters, (int)counters[SendEvents].TotalMilliseconds);
                    }

                    return BoolResult.Success;
                },
                counter: counters[SendEvents]);

            static void updateCountersWith(CounterCollection<ContentLocationEventStoreCounters> localCounters, ContentLocationEventData[] sentEvents)
            {
                foreach (var group in sentEvents.GroupBy(t => t.Kind))
                {
                    int eventCount = group.Count();
                    int hashCount = group.Sum(x => x.ContentHashes.Count);

                    switch (group.Key)
                    {
                        case EventKind.AddLocation:
                            localCounters[SentAddLocationsEvents].Add(eventCount);
                            localCounters[SentAddLocationsHashes].Add(hashCount);
                            break;
                        case EventKind.RemoveLocation:
                            localCounters[SentRemoveLocationsEvents].Add(eventCount);
                            localCounters[SentRemoveLocationsHashes].Add(hashCount);
                            break;
                        case EventKind.Touch:
                            localCounters[SentTouchLocationsEvents].Add(eventCount);
                            localCounters[SentTouchLocationsHashes].Add(hashCount);
                            break;
                        case EventKind.Blob:
                            localCounters[SentStoredEvents].Add(eventCount);
                            break;
                        case EventKind.UpdateMetadataEntry:
                            localCounters[SentUpdateMetadataEntryEvents].Add(eventCount);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException($"Unknown {nameof(EventKind)} '{group.Key}'.");
                    }
                }
            }
        }

        /// <nodoc />
        protected abstract Task<BoolResult> SendEventsCoreAsync(
            OperationContext context,
            ContentLocationEventData[] events,
            CounterCollection<ContentLocationEventStoreCounters> counters);

        /// <inheritdoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            EventNagleQueue = NagleQueue<(OperationContext context, ContentLocationEventData data)>.Create(
                // If nagle queue is triggered by time and has just one entry, we can use the context from that entry.
                // Otherwise we'll create a nested context.

                // The operation should fail if configured. This is important, because the flag to fail is set for reconciliation
                // and we want to fail reconciliation operation if we'll fail to send the data to event hub.
                input => SendEventsAsync(input.Length == 1 ? input[0].context : context.CreateNested(nameof(ContentLocationEventStore)), input.SelectArray(d => d.data))
                    .ThrowIfNeededAsync(_configuration.FailWhenSendEventsFails),
                maxDegreeOfParallelism: 1,
                interval: _configuration.EventNagleInterval,
                batchSize: _configuration.EventBatchSize);

            return BoolResult.SuccessTask;
        }

        /// <summary>
        /// Shuts down event queue and waits when all the pending messages are processed
        /// </summary>
        /// <remarks>
        /// This method is used during reconciliation because the reconciliation process should wait for all the events being processed
        /// before shutting down the store.
        /// </remarks>
        public async Task ShutdownEventQueueAndWaitForCompletionAsync()
        {
            var queue = Interlocked.Exchange(ref EventNagleQueue, null);
            if (queue != null)
            {
                await queue.DisposeAsync();
            }
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            EventNagleQueue?.Dispose();
            _workingDisposableDirectory.Dispose();

            return BoolResult.SuccessTask;
        }

        private EntryOperation GetOperation(ContentLocationEventData e)
        {
            switch (e.Kind)
            {
                case EventKind.AddLocation:
                    return EntryOperation.AddMachine;
                case EventKind.RemoveLocation:
                    return EntryOperation.RemoveMachine;
                case EventKind.Touch:
                    return EntryOperation.Touch;
                case EventKind.UpdateMetadataEntry:
                    return EntryOperation.UpdateMetadataEntry;
                default:
                    // NOTE: This is invalid because blob events should not have associated hashes
                    // The derived add/remove events will have the hashes
                    return EntryOperation.Invalid;
            }
        }

        /// <summary>
        /// Notifies about reconciliation of content
        /// </summary>
        public Task<BoolResult> ReconcileAsync(OperationContext context, MachineId machine, IReadOnlyList<ShortHashWithSize> addedContent, IReadOnlyList<ShortHash> removedContent, string suffix)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    // If under threshold just send reconcile events via normal events
                    if (addedContent.Count + removedContent.Count < LargeEventContentCountThreshold)
                    {
                        return AddLocations(context, machine, addedContent, reconciling: true) & RemoveLocations(context, machine, removedContent, reconciling: true);
                    }

                    await StoreAndPublishLargeEventStreamAsync(
                        context,
                        machine,
                        name: $"reconcile.{Environment.MachineName}.{machine.Index}{suffix}",
                        eventDatas: new ContentLocationEventData[]
                        {
                            new AddContentLocationEventData(machine, addedContent),
                            new RemoveContentLocationEventData(machine, removedContent)
                        }).ThrowIfFailure();

                    return BoolResult.Success;
                },
                Counters[PublishReconcile],
                extraEndMessage: _ => $"AddedContent={addedContent.Count}, RemovedContent={removedContent.Count}, TotalContent={addedContent.Count + removedContent.Count}");
        }


        /// <summary>
        /// Notifies about reconciliation of content
        /// </summary>
        public async Task<BoolResult> StoreAndPublishLargeEventStreamAsync(OperationContext context, MachineId machine, string name, IReadOnlyList<ContentLocationEventData> eventDatas)
        {
            return await context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    if (eventDatas.Count == 0)
                    {
                        return (0, "N/A");
                    }

                    var blobFilePath = _workingDirectory / $"event.{name}.blob";
                    var blobName = $"events/{name}.blob";

                    try
                    {
                        long size = 0;
                        using (Stream stream = await _fileSystem.OpenSafeAsync(blobFilePath, FileAccess.ReadWrite, FileMode.Create, FileShare.Read | FileShare.Delete, FileOptions.None, AbsFileSystemExtension.DefaultFileStreamBufferSize))
                        using (var writer = BuildXLWriter.Create(stream, leaveOpen: true))
                        {
                            EventDataSerializer.SerializeEvents(writer, eventDatas);
                            size = stream.Position;
                        }

                        // Uploading the checkpoint
                        var storageIdResult = await _storage.UploadFileAsync(context, blobFilePath, blobName).ThrowIfFailure();
                        var storageId = storageIdResult.Value;

                        Publish(context, new BlobContentLocationEventData(machine, storageId));

                        return Result.Success((size, storageId));
                    }
                    finally
                    {
                        _fileSystem.DeleteFile(blobFilePath);
                    }
                },
                Counters[PublishLargeEvent],
                extraEndMessage: r => $"Name={name}{resultToString(r)}");

            static string resultToString(Result<(long size, string? storageId)> result)
            {
                if (result)
                {
                    return $", Size={result.Value.size}, StorageId={result.Value.storageId}";
                }

                return string.Empty;
            }
        }

        /// <summary>
        /// Notify that the content hash list entry was updated.
        /// </summary>
        public Task<BoolResult> UpdateMetadataEntryAsync(OperationContext context, UpdateMetadataEntryEventData data)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    if ((data.Entry.ContentHashListWithDeterminism.ContentHashList?.Hashes.Count ?? 0) < LargeUpdateMetadataEventHashCountThreshold)
                    {
                        Publish(context, data);
                    }
                    else
                    {
                        await StoreAndPublishLargeEventStreamAsync(
                            context,
                            data.Sender,
                            name: $"metadata.{Environment.MachineName}.{Guid.NewGuid()}",
                            eventDatas: new[] { data }).ThrowIfFailure();
                    }

                    return BoolResult.Success;
                },
                Counters[PublishUpdateContentHashList]);
        }

        /// <summary>
        /// Notify that the content specified by the <paramref name="hashesWithSize"/> was added to the machine <paramref name="machine"/>.
        /// </summary>
        public BoolResult AddLocations(OperationContext context, MachineId machine, IReadOnlyList<ShortHashWithSize> hashesWithSize, bool reconciling = false, bool touch = true)
        {
            if (hashesWithSize.Count == 0)
            {
                return BoolResult.Success;
            }

            return context.PerformOperation(
                Tracer,
                () =>
                {
                    var hashes = hashesWithSize.SelectList(h => h.Hash);
                    var sizes = hashesWithSize.SelectList(h => h.Size);

                    var eventData = new AddContentLocationEventData(machine, hashes, sizes, touch) { Reconciling = reconciling };

                    Publish(context, eventData);

                    return BoolResult.Success;
                },
                Counters[PublishAddLocations],
                traceErrorsOnly: true);
        }

        /// <summary>
        /// Notify that the content specified by the <paramref name="hashesWithSize"/> was added to the machine <paramref name="machine"/>.
        /// </summary>
        public BoolResult AddLocations(OperationContext context, MachineId machine, IReadOnlyList<ContentHashWithSize> hashesWithSize, bool touch = true)
        {
            if (hashesWithSize.Count == 0)
            {
                return BoolResult.Success;
            }

            return AddLocations(context, machine, hashesWithSize.SelectList(h => new ShortHashWithSize(h.Hash, h.Size)), touch: touch);
        }

        /// <summary>
        /// Notify that the content specified by the <paramref name="hashes"/> was removed.
        /// </summary>
        public BoolResult RemoveLocations(OperationContext context, MachineId machine, IReadOnlyList<ContentHash> hashes)
        {
            if (hashes.Count == 0)
            {
                return BoolResult.Success;
            }

            return RemoveLocations(context, machine, hashes.SelectList(h => new ShortHash(h)));
        }

        /// <summary>
        /// Notify that the content specified by the <paramref name="hashes"/> was removed.
        /// </summary>
        public BoolResult RemoveLocations(OperationContext context, MachineId machine, IReadOnlyList<ShortHash> hashes, bool reconciling = false)
        {
            if (hashes.Count == 0)
            {
                return BoolResult.Success;
            }

            return context.PerformOperation(
                Tracer,
                () =>
                {
                    Publish(context, new RemoveContentLocationEventData(machine, hashes) { Reconciling = reconciling });
                    return BoolResult.Success;
                },
                Counters[PublishRemoveLocations],
                traceErrorsOnly: true);
        }

        /// <summary>
        /// Notify that the content specified by the <paramref name="hashes"/> was touched at <paramref name="accessTime"/>.
        /// </summary>
        public BoolResult Touch(OperationContext context, MachineId machine, IReadOnlyList<ContentHash> hashes, DateTime accessTime)
        {
            if (hashes.Count == 0)
            {
                return BoolResult.Success;
            }

            return context.PerformOperation(
                Tracer,
                () =>
                {
                    Publish(context, new TouchContentLocationEventData(machine, hashes.SelectList(h => new ShortHash(h)), accessTime));
                    return BoolResult.Success;
                },
                Counters[PublishTouchLocations],
                traceErrorsOnly: true);
        }

        /// <nodoc />
        public CounterSet GetCounters() => Counters.ToCounterSet();

        /// <summary>
        /// Pauses events notification and returns a disposable object that will resume event notification when <see cref="IDisposable.Dispose"/> method is called.
        /// </summary>
        public IDisposable PauseSendingEvents()
        {
            return new SendEventsSuspender(this);
        }

        /// <summary>
        /// Starts receiving events from the event store. 
        /// NOTE: This may be called event if the event store is already processing events. It is the responsibility of the event store to handle this appropriately.
        /// </summary>
        public BoolResult StartProcessing(OperationContext context, EventSequencePoint sequencePoint)
        {
            if (IsProcessing)
            {
                return BoolResult.Success;
            }

            var result = context.PerformOperation(
                Tracer,
                () => DoStartProcessing(context, sequencePoint),
                Counters[ContentLocationEventStoreCounters.StartProcessing]);

            if (result)
            {
                IsProcessing = true;
            }

            return result;
        }

        /// <summary>
        /// Stops receiving events from the event store. 
        /// NOTE: This may be called event if the event store is already processing events. It is the responsibility of the event store to handle this appropriately.
        /// </summary>
        public BoolResult SuspendProcessing(OperationContext context)
        {
            if (!IsProcessing)
            {
                return BoolResult.Success;
            }

            var result = context.PerformOperation(
                Tracer,
                () => DoSuspendProcessing(context),
                Counters[ContentLocationEventStoreCounters.SuspendProcessing]);

            if (result)
            {
                IsProcessing = false;
            }

            return result;
        }

        /// <nodoc />
        public abstract EventSequencePoint? GetLastProcessedSequencePoint();

        /// <summary>
        /// Gets whether the event store is currently receiving events
        /// </summary>
        private bool IsProcessing { get; set; }

        /// <nodoc />
        protected abstract BoolResult DoStartProcessing(OperationContext context, EventSequencePoint sequencePoint);

        /// <nodoc />
        protected abstract BoolResult DoSuspendProcessing(OperationContext context);

        private class SendEventsSuspender : IDisposable
        {
            private readonly ContentLocationEventStore _eventStore;
            private readonly IDisposable _nagleQueueSuspender;

            /// <nodoc />
            public SendEventsSuspender(ContentLocationEventStore eventStore)
            {
                _eventStore = eventStore;
                _nagleQueueSuspender = eventStore.EventNagleQueue!.Suspend();
            }

            /// <inheritdoc />
            public void Dispose()
            {
                if (!_eventStore.ShutdownStarted)
                {
                    // Resume event processing only if the current instance is still working normally and is not in a shut down state.
                    _nagleQueueSuspender.Dispose();
                }
            }
        }
    }
}
