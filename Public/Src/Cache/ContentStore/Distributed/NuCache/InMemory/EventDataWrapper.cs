// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System;
using Azure.Messaging.EventHubs;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.InMemory
{
    /// <summary>
    /// This class allows us to create instances of Azure.Messaging.EventHubs.EventData for test purposes
    /// <summary>
    public class EventDataWrapper : EventData
    {
        public EventDataWrapper(
            BinaryData eventBody,
            IDictionary<string, object> properties,
            IReadOnlyDictionary<string, object> systemProperties,
            long sequenceNumber,
            long offset,
            DateTimeOffset enqueuedTime,
            string partitionKey) : base(eventBody: eventBody, properties: properties, systemProperties: systemProperties, sequenceNumber: sequenceNumber, offset: offset, enqueuedTime: enqueuedTime, partitionKey: partitionKey)
        {
        }
    }
}
