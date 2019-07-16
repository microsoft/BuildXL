// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// Configuration type for <see cref="ContentLocationEventStore"/> family of types.
    /// </summary>
    public abstract class ContentLocationEventStoreConfiguration
    {
        /// <summary>
        /// The number of events which forces an event batch to be sent
        /// </summary>
        public int EventBatchSize { get; set; } = 200;

        /// <summary>
        /// The number of events which forces an event batch to be sent
        /// </summary>
        public TimeSpan EventNagleInterval { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// An epoch used for reseting event processing.
        /// </summary>
        public string Epoch { get; set; } = string.Empty;

        /// <summary>
        /// Specifies the delay of the first event processed after the epoch is reset.
        /// </summary>
        public TimeSpan NewEpochEventStartCursorDelay { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// If enabled, serialized entries would be deserialized back to make sure the serialization/deserialization process is correct.
        /// </summary>
        public bool SelfCheckSerialization { get; set; } = false;

        /// <summary>
        /// The max concurrency to use for events processing.
        /// </summary>
        public int MaxEventProcessingConcurrency { get; set; } = 1;

        /// <summary>
        /// The size of the queue used for concurrent event processing.
        /// </summary>
        public int EventProcessingMaxQueueSize { get; set; } = 100;
    }

    /// <summary>
    /// Configuration type for <see cref="MemoryEventHubClient"/>.
    /// </summary>
    public sealed class MemoryContentLocationEventStoreConfiguration : ContentLocationEventStoreConfiguration
    {
        /// <nodoc />
        public MemoryContentLocationEventStoreConfiguration()
        {
            EventBatchSize = 1;
            MaxEventProcessingConcurrency = 1;
        }

        /// <summary>
        /// Global in-memory event hub used for testing purposes.
        /// </summary>
        public MemoryEventHubClient.EventHub Hub { get; } = new MemoryEventHubClient.EventHub();
    }

    /// <summary>
    /// Configuration type for event hub-based content location event store.
    /// </summary>
    public sealed class EventHubContentLocationEventStoreConfiguration : ContentLocationEventStoreConfiguration
    {
        /// <inheritdoc />
        public EventHubContentLocationEventStoreConfiguration(
            string eventHubName,
            string eventHubConnectionString,
            string consumerGroupName,
            string epoch)
        {
            Contract.Requires(!string.IsNullOrEmpty(eventHubName));
            Contract.Requires(!string.IsNullOrEmpty(eventHubConnectionString));
            Contract.Requires(!string.IsNullOrEmpty(consumerGroupName));

            EventHubName = eventHubName;
            EventHubConnectionString = eventHubConnectionString;
            ConsumerGroupName = consumerGroupName;
            Epoch = epoch ?? string.Empty;
        }

        /// <summary>
        /// Event Hub name (a.k.a. Event Hub's entity path).
        /// </summary>
        public string EventHubName { get; }

        /// <nodoc />
        public string EventHubConnectionString { get; }

        /// <nodoc />
        public string ConsumerGroupName { get; }

        /// <summary>
        /// Creates another configuration instance with a given <paramref name="consumerGroupName"/>.
        /// </summary>
        public EventHubContentLocationEventStoreConfiguration WithConsumerGroupName(string consumerGroupName)
            => new EventHubContentLocationEventStoreConfiguration(EventHubName, EventHubConnectionString, consumerGroupName, Epoch);

    }
}
