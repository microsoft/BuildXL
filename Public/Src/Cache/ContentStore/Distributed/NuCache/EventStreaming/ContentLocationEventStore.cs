// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
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
        /// Indicates the maximum amount of content which will be sent via events vs storage for reconcilation.
        /// If under threshold, the events are sent via standard event streaming pipeline
        /// If over threshold, the events are serialized to storage instead and a single reconcile event is sent with storage id.
        /// </summary>
        public const int ReconcileContentCountEventThreshold = 10000;

        /// <inheritdoc />
        protected override Tracer Tracer { get; }

        /// <nodoc />
        protected readonly IContentLocationEventHandler EventHandler;

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
            Interfaces.FileSystem.AbsolutePath workingDirectory)
        {
            Contract.Requires(configuration != null);
            Contract.Requires(name != null);
            Contract.Requires(eventHandler != null);
            Contract.Requires(centralStorage != null);
            Contract.Requires(workingDirectory != null);

            _configuration = configuration!;
            _fileSystem = new PassThroughFileSystem();
            _storage = centralStorage!;
            _workingDisposableDirectory = new DisposableDirectory(_fileSystem, workingDirectory);
            _workingDirectory = workingDirectory!;
            EventHandler = eventHandler!;
            var tracer = new Tracer(name) { LogOperationStarted = false };
            Tracer = tracer;

            ValidationMode validationMode = configuration!.SelfCheckSerialization ? ValidationMode.Trace : ValidationMode.Off;
            EventDataSerializer = new ContentLocationEventDataSerializer(validationMode);
        }

        /// <summary>
        /// Factory method for creating an instance of <see cref="ContentLocationEventStore"/> based on <paramref name="configuration"/>.
        /// </summary>
        public static ContentLocationEventStore Create(
            ContentLocationEventStoreConfiguration configuration,
            IContentLocationEventHandler eventHandler,
            string localMachineName,
            CentralStorage centralStorage,
            Interfaces.FileSystem.AbsolutePath workingDirectory)
        {
            Contract.Requires(configuration != null);
            return new EventHubContentLocationEventStore(configuration!, eventHandler, localMachineName, centralStorage, workingDirectory);
        }

        /// <summary>
        /// Dispatch the <paramref name="eventData"/> to an event handler specified during instance construction.
        /// </summary>
        public Task DispatchAsync(OperationContext context, ContentLocationEventData eventData)
        {
            return DispatchAsync(context, eventData, Counters);
        }

        /// <nodoc />
        protected async Task DispatchAsync(OperationContext context, ContentLocationEventData eventData, CounterCollection<ContentLocationEventStoreCounters> counters)
        {
            Contract.Requires(eventData != null);

            switch (eventData)
            {
                case AddContentLocationEventData addContent:
                    using (counters[DispatchAddLocations].Start())
                    {
                        counters[DispatchAddLocationsHashes].Add(eventData.ContentHashes.Count);
                        EventHandler.LocationAdded(
                            context,
                            addContent.Sender,
                            addContent.ContentHashes.SelectList((hash, index) => new ShortHashWithSize(hash, addContent.ContentSizes[index])),
                            eventData.Reconciling);
                    }

                    break;
                case RemoveContentLocationEventData removeContent:
                    using (counters[DispatchRemoveLocations].Start())
                    {
                        counters[DispatchRemoveLocationsHashes].Add(eventData.ContentHashes.Count);
                        EventHandler.LocationRemoved(context, removeContent.Sender, removeContent.ContentHashes, eventData.Reconciling);
                    }

                    break;
                case TouchContentLocationEventData touchContent:
                    using (counters[DispatchTouch].Start())
                    {
                        counters[DispatchTouchHashes].Add(eventData.ContentHashes.Count);
                        EventHandler.ContentTouched(context, touchContent.Sender, touchContent.ContentHashes, touchContent.AccessTime);
                    }
                    break;
                case ReconcileContentLocationEventData reconcileContent:
                    using (counters[DispatchReconcile].Start())
                    {
                        await DeserializeAndDispatchReconcileEventAsync(context, reconcileContent, counters);
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unknown ContentLocationEventData type '{eventData!.GetType()}'.");
            }
        }

        private Task DeserializeAndDispatchReconcileEventAsync(OperationContext context, ReconcileContentLocationEventData reconcileContent, CounterCollection<ContentLocationEventStoreCounters> counters)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    IEnumerable<ContentLocationEventData> addOrRemoveEvents;

                    using (counters[GetAndDeserializeReconcileData].Start())
                    {
                        addOrRemoveEvents = await getAndDeserializeReconcileDataAsync();
                    }

                    int added = 0;
                    int removed = 0;

                    foreach (var addOrRemoveEvent in addOrRemoveEvents)
                    {
                        if (addOrRemoveEvent.Kind == EventKind.AddLocation)
                        {
                            added += addOrRemoveEvent.ContentHashes.Count;
                        }
                        else if (addOrRemoveEvent.Kind == EventKind.RemoveLocation)
                        {
                            removed += addOrRemoveEvent.ContentHashes.Count;
                        }

                        addOrRemoveEvent.Reconciling = true;

                        await DispatchAsync(context, addOrRemoveEvent, counters);
                    }

                    // We don't know what is the total number of processed entries after deserialization.
                    return new ReconciliationResult(addedCount: added, removedCount: removed, totalLocalContentCount: -1);
                }).ThrowIfFailure();

            async Task<IEnumerable<ContentLocationEventData>> getAndDeserializeReconcileDataAsync()
            {
                var reconcileFilePath = _workingDirectory / Guid.NewGuid().ToString();
                var blobName = reconcileContent.BlobId;

                await _storage.TryGetFileAsync(context, blobName, reconcileFilePath).ThrowIfFailure();

                using var stream = await _fileSystem.OpenSafeAsync(
                    reconcileFilePath,
                    FileAccess.Read,
                    FileMode.Open,
                    FileShare.Read | FileShare.Delete,
                    FileOptions.DeleteOnClose,
                    AbsFileSystemExtension.DefaultFileStreamBufferSize);
                using var reader = BuildXLReader.Create(stream, leaveOpen: true);
                // Calling ToList to force materialization of IEnumerable to avoid access of disposed stream.
                return EventDataSerializer.DeserializeReconcileData(reader).ToList();
            }
        }

        /// <nodoc />
        protected virtual void Publish(OperationContext context, ContentLocationEventData eventData)
        {
            EventNagleQueue?.Enqueue((context, eventData));
        }

        /// <nodoc />
        protected async Task SendEventsAsync(OperationContext context, ContentLocationEventData[] events)
        {
            context = context.CreateNested();

            Tracer.Info(context, $"{Tracer.Name}: Sending {events.Length} event(s) to event hub.");
            var operations = events.SelectMany(
                e =>
                {
                    var operation = GetOperation(e);
                    var reason = e.Reconciling ? OperationReason.Reconcile : OperationReason.Unknown;
                    return e.ContentHashes.Select(hash => (hash, operation, reason));
                }).ToList();
            LogContentLocationOperations(context, Tracer.Name, operations);

            var counters = new CounterCollection<ContentLocationEventStoreCounters>();

            // Using local counters instance for tracing purposes.
            counters[SentEventsCount].Add(events.Length);

            updateCountersWith(counters, events);

            var result = await context.PerformOperationAsync(
                Tracer,
                () => SendEventsCoreAsync(context, events, counters),
                traceOperationStarted: true,
                traceOperationFinished: true,
                counter: counters[SendEvents]);

            // Updating global counters based on the operation results.
            Counters.Append(counters);

            if (result)
            {
                // Trace successful case separately.
                context.LogSendEventsOverview(counters, (int)counters[SendEvents].Duration.TotalMilliseconds);
            }

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
                        case EventKind.Reconcile:
                            localCounters[SentReconcileEvents].Add(eventCount);
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
                input => SendEventsAsync(input.Length == 1 ? input[0].context : context.CreateNested(), input.SelectArray(d => d.data)),
                maxDegreeOfParallelism: 1,
                interval: _configuration.EventNagleInterval,
                batchSize: _configuration.EventBatchSize);

            return BoolResult.SuccessTask;
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
                default:
                    // NOTE: This is invalid because reconciliation events should not have associated hashes
                    // The derived add/remove events will have the hashes
                    return EntryOperation.Invalid;
            }
        }

        /// <summary>
        /// Notifies about reconciliation of content
        /// </summary>
        public Task<BoolResult> ReconcileAsync(OperationContext context, MachineId machine, IReadOnlyList<ShortHashWithSize> addedContent, IReadOnlyList<ShortHash> removedContent)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    // If under threshold just send reconcile events via normal events
                    if (addedContent.Count + removedContent.Count < ReconcileContentCountEventThreshold)
                    {
                        return AddLocations(context, machine, addedContent, reconciling: true) & RemoveLocations(context, machine, removedContent, reconciling: true);
                    }

                    var reconcileFilePath = _workingDirectory / $"reconcile.{Environment.MachineName}.{machine.Index}.blob";
                    var blobName = $"reconciles/{Environment.MachineName}.{machine.Index}.blob";

                    try
                    {
                        using (var stream = await _fileSystem.OpenSafeAsync(reconcileFilePath, FileAccess.ReadWrite, FileMode.Create, FileShare.Read | FileShare.Delete, FileOptions.None, AbsFileSystemExtension.DefaultFileStreamBufferSize))
                        using (var writer = BuildXLWriter.Create(stream, leaveOpen: true))
                        {
                            EventDataSerializer.SerializeReconcileData(context, writer, machine, addedContent, removedContent);
                        }

                        // Uploading the checkpoint
                        var storageIdResult = await _storage.UploadFileAsync(context, reconcileFilePath, blobName).ThrowIfFailure();
                        var storageId = storageIdResult.Value;

                        Publish(context, new ReconcileContentLocationEventData(machine, storageId));

                        return BoolResult.Success;
                    }
                    finally
                    {
                        _fileSystem.DeleteFile(reconcileFilePath);
                    }
                },
                Counters[PublishReconcile],
                extraEndMessage: _ => $"AddedContent={addedContent.Count}, RemovedContent={removedContent.Count}, TotalContent={addedContent.Count + removedContent.Count}");
        }

        /// <summary>
        /// Notify that the content specified by the <paramref name="hashesWithSize"/> was added to the machine <paramref name="machine"/>.
        /// </summary>
        public BoolResult AddLocations(OperationContext context, MachineId machine, IReadOnlyList<ShortHashWithSize> hashesWithSize, bool reconciling = false)
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

                    Publish(context, new AddContentLocationEventData(machine, hashes, sizes) { Reconciling = reconciling });

                    return BoolResult.Success;
                },
                Counters[PublishAddLocations],
                traceErrorsOnly: true);
        }

        /// <summary>
        /// Notify that the content specified by the <paramref name="hashesWithSize"/> was added to the machine <paramref name="machine"/>.
        /// </summary>
        public BoolResult AddLocations(OperationContext context, MachineId machine, IReadOnlyList<ContentHashWithSize> hashesWithSize)
        {
            if (hashesWithSize.Count == 0)
            {
                return BoolResult.Success;
            }

            return AddLocations(context, machine, hashesWithSize.SelectList(h => new ShortHashWithSize(h.Hash, h.Size)));
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
