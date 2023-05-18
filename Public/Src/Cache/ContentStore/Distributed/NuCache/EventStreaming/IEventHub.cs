// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using Microsoft.Azure.EventHubs;

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
}
