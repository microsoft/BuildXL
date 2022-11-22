// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// Configuration type for <see cref="ContentLocationEventStore"/> family of types.
    /// </summary>
    public abstract record ContentLocationEventStoreConfiguration
    {
        /// <summary>
        /// The number of events which forces an event batch to be sent
        /// </summary>
        public int EventBatchSize { get; set; } = 1000;

        /// <summary>
        /// A time to flush the message before shutting the pipeline down.
        /// </summary>
        /// <remarks>
        /// 2 seconds should be enough because 99-th percentile for sending the events through event hub is 1.5 seconds in prod.
        /// </remarks>
        public TimeSpan? FlushShutdownTimeout { get; set; }

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
        /// If enabled, self-check serialization failures will trigger an error instead of just getting traced.
        /// </summary>
        public bool SelfCheckSerializationShouldFail { get; set; } = false;

        /// <summary>
        /// The max concurrency to use for events processing.
        /// </summary>
        public int MaxEventProcessingConcurrency { get; set; } = 1;

        /// <summary>
        /// The size of the queue used for concurrent event processing.
        /// </summary>
        public int EventProcessingMaxQueueSize { get; set; } = 10000;

        /// <summary>
        /// If true, the nagle queue used by the event store will stop processing events and the error will be propagated back to the caller in Dispose method.
        /// </summary>
        public bool FailWhenSendEventsFails { get; set; } = false;
    }

    /// <summary>
    /// Configuration type for <see cref="NullEventHubClient"/>.
    /// </summary>
    public record NullContentLocationEventStoreConfiguration : ContentLocationEventStoreConfiguration
    {
    }

    /// <summary>
    /// Configuration type for <see cref="MemoryEventHubClient"/>.
    /// </summary>
    public record MemoryContentLocationEventStoreConfiguration : ContentLocationEventStoreConfiguration
    {
        /// <nodoc />
        public MemoryContentLocationEventStoreConfiguration()
        {
            EventBatchSize = 1;
            MaxEventProcessingConcurrency = 1;

            // Since all tests run with in-memory EventHub, we force them to run with self-check by default, which
            // helps catch errors (i.e. serialization, race conditions) early on.
            SelfCheckSerialization = true;
            SelfCheckSerializationShouldFail = true;
        }

        /// <summary>
        /// Global in-memory event hub used for testing purposes.
        /// </summary>
        public MemoryEventHubClient.EventHub Hub { get; } = new MemoryEventHubClient.EventHub();
    }

    /// <summary>
    /// Configuration type for event hub-based content location event store.
    /// </summary>
    public record EventHubContentLocationEventStoreConfiguration : ContentLocationEventStoreConfiguration
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
