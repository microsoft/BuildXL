// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using Azure.Messaging.EventHubs;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// Client for an event hub service.
    /// </summary>
    public interface IEventHubClient : IStartupShutdownSlim
    {
        /// <summary>
        /// Start receiving events to be processed.
        /// </summary>
        BoolResult StartProcessing(OperationContext context, EventSequencePoint sequencePoint, IPartitionReceiveHandler processor);

        /// <summary>
        /// Stop receiving events.
        /// </summary>
        BoolResult SuspendProcessing(OperationContext context);

        /// <summary>
        /// Send an event.
        /// </summary>
        Task SendAsync(OperationContext context, EventData eventData);
    }

    /// <summary>
    /// No-op implementation of event hub client
    /// </summary>
    public class NullEventHubClient : StartupShutdownSlimBase, IEventHubClient
    {
        /// <nodoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(NullEventHubClient));

        /// <nodoc />
        public Task SendAsync(OperationContext context, EventData eventData)
        {
            return Task.CompletedTask;
        }

        /// <nodoc />
        public BoolResult StartProcessing(OperationContext context, EventSequencePoint sequencePoint, IPartitionReceiveHandler processor)
        {
            return BoolResult.Success;
        }

        /// <nodoc />
        public BoolResult SuspendProcessing(OperationContext context)
        {
            return BoolResult.Success;
        }
    }

    /// <summary>
    /// A handler interface for the receive operation for Azure.Messaging.EventHubs
    /// <summary>
    public interface IPartitionReceiveHandler
    {
        /// <summary>
        /// Gets or sets the maximum batch size.
        /// </summary>
        int MaxBatchSize { get; set; }

        /// <summary>
        /// Implement this method to specify the action to be performed on the received events.
        /// </summary>
        Task ProcessEventsAsync(System.Collections.Generic.IEnumerable<EventData> events);

        /// <summary>
        /// Implement in order to handle exceptions that are thrown during receipt of events.
        /// </summary>
        Task ProcessErrorAsync(System.Exception error);
    }
}
