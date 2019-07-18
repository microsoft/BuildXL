// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
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
}
