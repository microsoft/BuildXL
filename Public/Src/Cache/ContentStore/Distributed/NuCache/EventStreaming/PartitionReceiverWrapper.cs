// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using System.Threading;
using Azure.Core;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Primitives;
using System;
using Azure.Messaging.EventHubs;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using Azure;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    // Class based on https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/eventhub/Microsoft.Azure.EventHubs/src/Amqp/AmqpPartitionReceiver.cs (legacy Microsoft.Azure.EventHubs library)
    internal class PartitionReceiverWrapper : PartitionReceiver
    {
        private readonly object _receivePumpLock = new object();
        private IPartitionReceiveHandler _receiveHandler;
        private Task _receivePumpTask;
        private CancellationTokenSource _receivePumpCancellationSource;
        private const int ReceiveHandlerDefaultBatchSize = 10;

        protected Tracer Tracer { get; } = new Tracer(nameof(PartitionReceiverWrapper));

        public PartitionReceiverWrapper(string consumerGroup,
                                        string partitionId,
                                        EventPosition eventPosition,
                                        string fullyQualifiedNamespace,
                                        string eventHubName,
                                        TokenCredential credential) : base(consumerGroup, partitionId, eventPosition, fullyQualifiedNamespace, eventHubName, credential)
        {
        }

        public PartitionReceiverWrapper(string consumerGroup,
                                        string partitionId,
                                        EventPosition eventPosition,
                                        string connectionString,
                                        string eventHubName) : base(consumerGroup, partitionId, eventPosition, connectionString, eventHubName)
        {
        }

        public PartitionReceiverWrapper(string consumerGroup,
                                string partitionId,
                                EventPosition eventPosition,
                                string fullyQualifiedNamespace,
                                string eventHubName,
                                AzureSasCredential credential) : base(consumerGroup, partitionId, eventPosition, fullyQualifiedNamespace, eventHubName, credential)
        {
        }

        public void SetReceiveHandler(OperationContext context, IPartitionReceiveHandler newReceiveHandler)
        {
            lock (_receivePumpLock)
            {
                if (newReceiveHandler != null)
                {
                    _receiveHandler = newReceiveHandler;

                    // We have a new receiveHandler, ensure pump is running.
                    if (_receivePumpTask == null)
                    {
                        _receivePumpCancellationSource = new CancellationTokenSource();
                        _receivePumpTask = ReceivePumpAsync(context, _receivePumpCancellationSource.Token);
                    }
                }
            }
        }

        public override async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            await ReceiveHandlerClose().ConfigureAwait(false);
            await base.CloseAsync();
        }

        private Task ReceiveHandlerClose()
        {
            Task task = null;

            lock (_receivePumpLock)
            {
                if (_receiveHandler != null)
                {
                    if (_receivePumpTask != null)
                    {
                        task = _receivePumpTask;
                        _receivePumpCancellationSource.Cancel();
                        _receivePumpCancellationSource.Dispose();
                        _receivePumpCancellationSource = null;
                        _receivePumpTask = null;
                    }

                    _receiveHandler = null;
                }
            }

            return task ?? Task.CompletedTask;
        }

        private async Task ReceivePumpAsync(OperationContext context, CancellationToken cancellationToken)
        {
            try
            {
                // Loop until pump is shutdown or an error is hit.
                while (!cancellationToken.IsCancellationRequested)
                {
                    IEnumerable<EventData> receivedEvents;

                    try
                    {
                        int batchSize;

                        lock (_receivePumpLock)
                        {
                            if (_receiveHandler == null)
                            {
                                // Pump has been shutdown, nothing more to do.
                                return;
                            }

                            batchSize = _receiveHandler.MaxBatchSize > 0 ? _receiveHandler.MaxBatchSize : ReceiveHandlerDefaultBatchSize;
                        }

                        receivedEvents = await ReceiveBatchAsync(batchSize, cancellationToken).ConfigureAwait(false);
                    }
                    catch (EventHubsException e) when (e.Reason == EventHubsException.FailureReason.ConsumerDisconnected)
                    {
                        // ConsumerDisconnectedException is a special case where we know we cannot recover the pump.
                        break;
                    }
                    catch (Exception e)
                    {
                        Tracer.Error(context, e, "Error during ReceiveBatchAsync");
                        continue;
                    }

                    if (receivedEvents != null)
                    {
                        try
                        {
                            await ReceiveHandlerProcessEventsAsync(receivedEvents).ConfigureAwait(false);
                        }
                        catch (Exception userCodeError)
                        {
                            await ReceiveHandlerProcessErrorAsync(userCodeError).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // This should never throw
                Tracer.Error(context, ex, "EventHub ReceivePumpAsync failed");
            }
        }

        // Encapsulates taking the receivePumpLock, checking this.receiveHandler for null,
        // calls this.receiveHandler.ProcessEventsAsync (starting this operation inside the ReceivePumpAsync).
        private Task ReceiveHandlerProcessEventsAsync(IEnumerable<EventData> eventDatas)
        {
            Task processEventsTask = null;

            lock (_receivePumpLock)
            {
                if (_receiveHandler != null)
                {
                    processEventsTask = _receiveHandler.ProcessEventsAsync(eventDatas);
                }
            }

            return processEventsTask ?? Task.FromResult(0);
        }

        // Encapsulates taking the receivePumpLock, checking this.receiveHandler for null,
        // calls this.receiveHandler.ProcessErrorAsync (starting this operation inside the ReceivePumpAsync).
        private Task ReceiveHandlerProcessErrorAsync(Exception error)
        {
            Task processErrorTask = null;
            lock (_receivePumpLock)
            {
                if (_receiveHandler != null)
                {
                    processErrorTask = _receiveHandler.ProcessErrorAsync(error);
                }
            }

            return processErrorTask ?? Task.FromResult(0);
        }

    }
}
