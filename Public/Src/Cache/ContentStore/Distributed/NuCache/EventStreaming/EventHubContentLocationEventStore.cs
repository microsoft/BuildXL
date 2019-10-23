// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Microsoft.Azure.EventHubs;
using Microsoft.Practices.TransientFaultHandling;
using static BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming.ContentLocationEventStoreCounters;
using RetryPolicy = Microsoft.Practices.TransientFaultHandling.RetryPolicy;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// Event store that uses Azure Event Hub for event propagation.
    /// </summary>
    public sealed class EventHubContentLocationEventStore : ContentLocationEventStore
    {
        private const string EventProcessingDelayInSecondsMetricName = nameof(EventProcessingDelayInSecondsMetricName);

        private readonly ContentLocationEventStoreConfiguration _configuration;
        private readonly string _localMachineName;

        private const string SenderMachineKey = "SenderMachine";
        private const string EventFilterKey = "Epoch";
        private const string OperationIdKey = "OperationId";

        private readonly IEventHubClient _eventHubClient;
        private readonly RetryPolicy _extraEventHubClientRetryPolicy;

        private Processor? _currentEventProcessor;
        private readonly ActionBlock<ProcessEventsInput>[]? _eventProcessingBlocks;

        private EventSequencePoint? _lastProcessedSequencePoint;

        private int _updatingPendingEventProcessingStates = 0;

        /// <summary>
        /// We use a queue to ensure that <see cref="_lastProcessedSequencePoint"/> is updated in such a way that
        /// it is never set to a value where messages prior to that sequence number have not been processed. Naively,
        /// setting this value, as messages are processed could break this criteria because of concurrent event processing.
        /// Given that, message batch state (with associated sequence number) are put into queue in order messages are received,
        /// and only dequeued (and used to update <see cref="_lastProcessedSequencePoint"/>) when all messages associated with the
        /// batch have been processed. Thereby, ensuring <see cref="_lastProcessedSequencePoint"/> is updated in correct order.
        /// </summary>
        private ConcurrentQueue<SharedEventProcessingState> _pendingEventProcessingStates = new ConcurrentQueue<SharedEventProcessingState>();

        private long _queueSize;

        /// <inheritdoc />
        public EventHubContentLocationEventStore(
            ContentLocationEventStoreConfiguration configuration,
            IContentLocationEventHandler eventHandler,
            string localMachineName,
            CentralStorage centralStorage,
            Interfaces.FileSystem.AbsolutePath workingDirectory)
            : base(configuration, nameof(EventHubContentLocationEventStore), eventHandler, centralStorage, workingDirectory)
        {
            Contract.Requires(configuration.MaxEventProcessingConcurrency >= 1);

            _configuration = configuration;
            _localMachineName = localMachineName;
            _eventHubClient = CreateEventHubClient(configuration);
            _extraEventHubClientRetryPolicy = CreateEventHubClientRetryPolicy();

            if (configuration.MaxEventProcessingConcurrency > 1)
            {
                _eventProcessingBlocks =
                    Enumerable.Range(1, configuration.MaxEventProcessingConcurrency)
                        .Select(
                            (_, index) =>
                            {
                                var serializer = new ContentLocationEventDataSerializer(configuration.SelfCheckSerialization ? ValidationMode.Trace : ValidationMode.Off);
                                return new ActionBlock<ProcessEventsInput>(
                                    t => ProcessEventsCoreAsync(t, serializer),
                                    new ExecutionDataflowBlockOptions()
                                    {
                                        MaxDegreeOfParallelism = 1,
                                        BoundedCapacity = configuration.EventProcessingMaxQueueSize,
                                    });
                            })
                        .ToArray();
            }
        }

        /// <summary>
        /// Factory method for creating an instance of <see cref="ContentLocationEventStore"/> based on <paramref name="configuration"/>.
        /// </summary>
        public static IEventHubClient CreateEventHubClient(ContentLocationEventStoreConfiguration configuration)
        {
            Contract.Requires(configuration != null);

            switch (configuration)
            {
                case EventHubContentLocationEventStoreConfiguration azureConfig:
                return new AzureEventHubClient(azureConfig);
                case MemoryContentLocationEventStoreConfiguration memoryConfig:
                return new MemoryEventHubClient(memoryConfig);
                default:
                throw new InvalidOperationException($"Unknown EventStore type '{configuration!.GetType()}'.");
            }
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            Tracer.Info(context, $"Initializing Event Hub-based content location event store with epoch '{_configuration.Epoch}'.");

            var baseInitializeResult = await base.StartupCoreAsync(context);
            if (!baseInitializeResult)
            {
                return baseInitializeResult;
            }

            _currentEventProcessor = new Processor(context, this);

            await _eventHubClient.StartupAsync(context).ThrowIfFailure();

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            // Need to dispose nagle queue first to ensure last batch is processed before buffers are disposed
            var result = await base.ShutdownCoreAsync(context);

            await _eventHubClient.ShutdownAsync(context).ThrowIfFailure();

            if (_eventProcessingBlocks != null)
            {
                foreach (var eventProcessingBlock in _eventProcessingBlocks)
                {
                    eventProcessingBlock.Complete();
                    await eventProcessingBlock.Completion;
                }
            }

            return result;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> SendEventsCoreAsync(
            OperationContext context,
            ContentLocationEventData[] events,
            CounterCollection<ContentLocationEventStoreCounters> counters)
        {
            IReadOnlyList<EventData> eventDatas;
            using (counters[Serialization].Start())
            {
                eventDatas = SerializeEventData(context, events);
            }

            var operationId = Guid.NewGuid();
            context = context.CreateNested(operationId);

            for (var eventNumber = 0; eventNumber < eventDatas.Count; eventNumber++)
            {
                var eventData = eventDatas[eventNumber];
                eventData.Properties[EventFilterKey] = _configuration.Epoch;
                eventData.Properties[SenderMachineKey] = _localMachineName;
                counters[SentEventBatchCount].Increment();

                Tracer.Info(
                    context,
                    $"{Tracer.Name}: Sending {eventNumber}/{events.Length} event. OpId={operationId}, Epoch='{_configuration.Epoch}', Size={eventData.Body.Count}.");
                counters[SentMessagesTotalSize].Add(eventData.Body.Count);
                eventData.Properties[OperationIdKey] = operationId.ToString();

                // Even though event hub client has it's own built-in retry strategy, we have to wrap all the calls into a separate
                // one to cover a few more important cases that the default strategy misses.
                await _extraEventHubClientRetryPolicy.ExecuteAsync(async () =>
                {
                    try
                    {
                        await _eventHubClient.SendAsync(context, eventData);
                    }
                    catch (ServerBusyException exception)
                    {
                        // TODO: Verify that the HResult is 50002. Documentation shows that this should be the error code for throttling,
                        // but documentation is done for Microsoft.ServiceBus.Messaging.ServerBusyException and not Microsoft.Azure.EventHubs.ServerBusyException
                        // https://docs.microsoft.com/en-us/azure/event-hubs/event-hubs-messaging-exceptions#serverbusyexception
                        Tracer.Debug(context, $"{Tracer.Name}: OpId={operationId} was throttled by EventHub. HResult={exception.HResult}");
                        Tracer.TrackMetric(context, "EventHubThrottle", 1);

                        throw;
                    }
                    catch (Exception e)
                    {
                        // If the error is not retryable, then the entire operation will fail and we don't need to double trace the error.
                        if (TransientEventHubErrorDetectionStrategy.IsRetryable(e))
                        {
                            Tracer.Debug(context, $"{Tracer.Name}.{nameof(SendEventsCoreAsync)} failed with retryable error=[{e}]");
                        }

                        throw;
                    }
                });
            }

            return BoolResult.Success;
        }

        private IReadOnlyList<EventData> SerializeEventData(OperationContext context, ContentLocationEventData[] events)
        {
            return EventDataSerializer.Serialize(context, events);
        }

        private async Task ProcessEventsAsync(OperationContext context, List<EventData> messages)
        {
            // Creating nested context for all the processing operations.
            context = context.CreateNested();

            if (messages.Count == 0)
            {
                // This probably does not actually occur, but just in case, ignore empty message batch.
                // NOTE: We do this after logging to ensure we notice if the we are getting empty message batches.
                return;
            }

            var state = new SharedEventProcessingState(context, this, messages);

            if (_eventProcessingBlocks != null)
            {
                await context
                    .CreateOperation(Tracer, () => sendToActionBlockAsync())
                    .WithOptions(traceOperationStarted: false, endMessageFactory: r => $"TotalQueueSize={Interlocked.Read(ref _queueSize)}")
                    .RunAsync(caller: "SendToActionBlockAsync")
                    .TraceIfFailure(context);
            }
            else
            {
                await ProcessEventsCoreAsync(new ProcessEventsInput(state, messages, actionBlockIndex: -1, store: this), EventDataSerializer);
            }

            async Task<BoolResult> sendToActionBlockAsync()
            {
                // This local function "sends" a message into an action block based on the sender's hash code to process events in parallel from different machines.
                // (keep in mind, that the data from the same machine should be processed sequentially, because events order matters).
                // Then, it creates a local counter for each processing operation to track the results for the entire batch.
                foreach (var messageGroup in messages.GroupBy(GetProcessingIndex))
                {
                    int actionBlockIndex = messageGroup.Key;
                    var eventProcessingBlock = _eventProcessingBlocks![actionBlockIndex];
                    var input = new ProcessEventsInput(state, messageGroup, actionBlockIndex, this);

                    var sendAsyncTask = eventProcessingBlock.SendAsync(input);
                    if (sendAsyncTask.Status == TaskStatus.WaitingForActivation)
                    {
                        // The action block is busy. It means that its most likely full.
                        Tracer.Debug(context, $"Action block {actionBlockIndex} is busy. Block's queue size={eventProcessingBlock.InputCount}.");

                    }
                    bool success = await sendAsyncTask;

                    if (!success)
                    {
                        // NOTE: This case should not actually occur.
                        // Complete the operation in case we couldn't send to the action block to prevent pending event queue from getting backlogged.
                        input.Complete();
                        return new BoolResult("Failed to add message to an action block.");
                    }
                }

                return BoolResult.Success;
            }
        }

        private int GetProcessingIndex(EventData message)
        {
            var sender = TryGetMessageSender(message);
            if (message == null)
            {
                Counters[MessagesWithoutSenderMachine].Increment();
            }

            sender ??= string.Empty;

            return Math.Abs(sender.GetHashCode()) % _eventProcessingBlocks!.Length;
        }

        private string? TryGetMessageSender(EventData message)
        {
            message.Properties.TryGetValue(SenderMachineKey, out var sender);
            return sender?.ToString();
        }

        private async Task ProcessEventsCoreAsync(ProcessEventsInput input, ContentLocationEventDataSerializer eventDataSerializer)
        {
            var context = input.State.Context;
            var counters = input.State.EventStoreCounters;

            try
            {
                await context.PerformOperationAsync(
                    Tracer,
                    async () =>
                    {
                        foreach (var message in input.Messages)
                        {
                            // Extracting information from the message
                            var foundEventFilter = message.Properties.TryGetValue(EventFilterKey, out var eventFilter);

                            message.Properties.TryGetValue(OperationIdKey, out var operationId);

                            var sender = TryGetMessageSender(message) ?? "Unknown sender";

                            var eventTimeUtc = message.SystemProperties.EnqueuedTimeUtc;
                            var eventProcessingDelay = DateTime.UtcNow - eventTimeUtc;

                            // Creating nested context with operationId as a guid. This helps to correlate operations on a worker and a master machines.
                            context = CreateNestedContext(context, operationId?.ToString());

                            Tracer.Info(context, $"{Tracer.Name}.ReceivedEvent: ProcessingDelay={eventProcessingDelay}, Sender={sender}, OpId={operationId}, SeqNo={message.SystemProperties.SequenceNumber}, EQT={eventTimeUtc}, Filter={eventFilter}, Size={message.Body.Count}.");

                            Tracer.TrackMetric(context, EventProcessingDelayInSecondsMetricName, (long)eventProcessingDelay.TotalSeconds);

                            counters[ReceivedMessagesTotalSize].Add(message.Body.Count);
                            counters[ReceivedEventBatchCount].Increment();

                            if (!foundEventFilter || !string.Equals(eventFilter as string, _configuration.Epoch))
                            {
                                counters[FilteredEvents].Increment();
                                continue;
                            }

                            // Deserializing a message
                            IReadOnlyList<ContentLocationEventData> eventDatas;

                            using (counters[Deserialization].Start())
                            {
                                eventDatas = eventDataSerializer.DeserializeEvents(message);
                            }

                            counters[ReceivedEventsCount].Add(eventDatas.Count);

                            // Dispatching deserialized events data
                            using (counters[DispatchEvents].Start())
                            {
                                foreach (var eventData in eventDatas)
                                {
                                    // An event processor may fail to process the event, but we will save the sequence point anyway.
                                    await DispatchAsync(context, eventData, counters);
                                }
                            }
                        }

                        return BoolResult.Success;
                    },
                    counters[ProcessEvents],
                    extraStartMessage: $"QueueIdx={input.ActionBlockIndex}, QueueSize={input.EventProcessingBlock?.InputCount}",
                    extraEndMessage: _ => $"QueueIdx={input.ActionBlockIndex}, QueueSize={input.EventProcessingBlock?.InputCount}, LocalDelay={DateTime.UtcNow - input.LocalEnqueueTime}"
                    ).IgnoreFailure(); // The error is logged
            }
            finally
            {
                // Complete the operation
                input.Complete();
            }
        }

        private static OperationContext CreateNestedContext(OperationContext context, string? operationId)
        {
            if (!Guid.TryParse(operationId, out var guid))
            {
                guid = Guid.NewGuid();
            }

            return context.CreateNested(guid);
        }

        /// <inheritdoc />
        public override EventSequencePoint? GetLastProcessedSequencePoint()
        {
            UpdatingPendingEventProcessingStates();
            return _lastProcessedSequencePoint;
        }

        private void UpdatingPendingEventProcessingStates()
        {
            // Prevent concurrent access to dequeuing from the queue and updating the last processed sequence point
            if (Interlocked.CompareExchange(ref _updatingPendingEventProcessingStates, value: 1, comparand: 0) == 0)
            {
                var pendingEventProcessingStates = _pendingEventProcessingStates;

                // Look at top event on queue, to see if it is complete, and dequeue and set as last processed event if it is. Otherwise,
                // just exit.
                while (pendingEventProcessingStates.TryPeek(out var peekPendingEventProcessingState))
                {
                    if (peekPendingEventProcessingState.IsComplete)
                    {
                        bool found = pendingEventProcessingStates.TryDequeue(out var pendingEventProcessingState);
                        Contract.Assert(found, "There should be no concurrent access to _pendingEventProcessingStates, so after peek a state should be dequeued.");
                        Contract.Assert(peekPendingEventProcessingState == pendingEventProcessingState, "There should be no concurrent access to _pendingEventProcessingStates, so the state for peek and dequeue should be the same.");

                        _lastProcessedSequencePoint = new EventSequencePoint(pendingEventProcessingState!.SequenceNumber);
                    }
                    else
                    {
                        // Top event batch on queue is not complete, no need to continue.
                        break;
                    }
                }

                Volatile.Write(ref _updatingPendingEventProcessingStates, 0);
            }
        }

        /// <inheritdoc />
        protected override BoolResult DoStartProcessing(OperationContext context, EventSequencePoint sequencePoint)
        {
            _pendingEventProcessingStates = new ConcurrentQueue<SharedEventProcessingState>();
            _eventHubClient.StartProcessing(context, sequencePoint, _currentEventProcessor).ThrowIfFailure();

            _lastProcessedSequencePoint = sequencePoint;
            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override BoolResult DoSuspendProcessing(OperationContext context)
        {
            // TODO: Make these async (bug 1365340)
            _eventHubClient.SuspendProcessing(context).ThrowIfFailure();
            _pendingEventProcessingStates = new ConcurrentQueue<SharedEventProcessingState>();
            return BoolResult.Success;
        }

        private RetryPolicy CreateEventHubClientRetryPolicy()
        {
            return new RetryPolicy(
                new TransientEventHubErrorDetectionStrategy(),
                RetryStrategy.DefaultExponential);
        }

        private class Processor : IPartitionReceiveHandler
        {
            private readonly EventHubContentLocationEventStore _store;
            private readonly OperationContext _context;

            public Processor(OperationContext context, EventHubContentLocationEventStore store)
            {
                _store = store;
                _context = context;

                MaxBatchSize = 100;
            }

            /// <inheritdoc />
            public Task ProcessEventsAsync(IEnumerable<EventData> events)
            {
                return _store.ProcessEventsAsync(_context, events.ToList());
            }

            /// <inheritdoc />
            public Task ProcessErrorAsync(Exception error)
            {
                _store.Tracer.Error(_context, $"EventHubProcessor.ProcessErrorAsync: error=[{error}].");
                return BoolTask.True;
            }

            /// <inheritdoc />
            public int MaxBatchSize { get; set; }
        }

        private class SharedEventProcessingState
        {
            private int _remainingMessageCount;
            private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

            public long SequenceNumber { get; }
            public OperationContext Context { get; }
            public EventHubContentLocationEventStore Store { get; }
            public CounterCollection<ContentLocationEventStoreCounters> EventStoreCounters { get; } = new CounterCollection<ContentLocationEventStoreCounters>();

            public bool IsComplete => _remainingMessageCount == 0;

            public SharedEventProcessingState(
                OperationContext context,
                EventHubContentLocationEventStore store,
                List<EventData> messages)
            {
                Context = context;
                Store = store;
                SequenceNumber = messages[messages.Count - 1].SystemProperties.SequenceNumber;
                _remainingMessageCount = messages.Count;
                store._pendingEventProcessingStates.Enqueue(this);
            }

            public void Complete(int messageCount)
            {
                if (Interlocked.Add(ref _remainingMessageCount, -messageCount) == 0)
                {
                    int duration = (int)_stopwatch.ElapsedMilliseconds;
                    Store.UpdatingPendingEventProcessingStates();
                    Context.LogProcessEventsOverview(EventStoreCounters, duration);

                    Store.Counters.Append(EventStoreCounters);
                }
            }
        }

        private class ProcessEventsInput
        {
            private readonly EventHubContentLocationEventStore _store;

            public DateTime LocalEnqueueTime { get; } = DateTime.UtcNow;

            public SharedEventProcessingState State { get; }

            public IEnumerable<EventData> Messages { get; }

            public int ActionBlockIndex { get; }

            public ActionBlock<ProcessEventsInput>? EventProcessingBlock =>
                ActionBlockIndex != -1 ? _store._eventProcessingBlocks![ActionBlockIndex] : null;

            /// <nodoc />
            public ProcessEventsInput(
                SharedEventProcessingState state,
                IEnumerable<EventData> messages,
                int actionBlockIndex,
                EventHubContentLocationEventStore store)
            {
                State = state;
                Messages = messages;
                ActionBlockIndex = actionBlockIndex;
                _store = store;
                Interlocked.Increment(ref store._queueSize);
            }

            public void Complete()
            {
                Interlocked.Decrement(ref _store._queueSize);
                State.Complete(Messages.Count());
            }
        }

        private class TransientEventHubErrorDetectionStrategy : ITransientErrorDetectionStrategy
        {
            /// <inheritdoc />
            public bool IsTransient(Exception ex)
            {
                return IsRetryable(ex);
            }

            public static bool IsRetryable(Exception exception)
            {
                if (exception is AggregateException ae)
                {
                    return ae.InnerExceptions.All(e => IsRetryable(e));
                }

                if (Microsoft.Azure.EventHubs.RetryPolicy.IsRetryableException(exception))
                {
                    return true;
                }

                // IsRetryableException covers TaskCanceledException, EventHubException, OperationCanceledException and SocketException

                // Need to cover some additional cases here.
                if (exception is TimeoutException || exception is ServerBusyException)
                {
                    return true;
                }

                return false;
            }
        }
    }
}
