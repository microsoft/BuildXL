// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json.Serialization;
using Microsoft.Azure.EventHubs;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// Represents event sequence number or a date time used to create an event position in event hub.
    /// </summary>
    public record EventSequencePoint
    {
        public static EventSequencePoint Invalid { get; } = new();

        /// <summary>
        /// Sequence number for processing events starting from a given point.
        /// </summary>
        public long? SequenceNumber { get; init; }

        /// <nodoc />
        public DateTime? EventStartCursorTimeUtc { get; init; }

        /// <nodoc />
        [JsonIgnore]
        public EventPosition EventPosition => SequenceNumber != null
            ? EventPosition.FromSequenceNumber(SequenceNumber.Value)
            : EventPosition.FromEnqueuedTime(EventStartCursorTimeUtc.Value);

        /// <nodoc />
        public EventSequencePoint()
            : this(eventStartCursorTimeUtc: DateTime.MinValue)
        {
        }

        /// <nodoc />
        public EventSequencePoint(long sequenceNumber)
        {
            SequenceNumber = sequenceNumber;
        }

        /// <nodoc />
        public EventSequencePoint(DateTime eventStartCursorTimeUtc)
        {
            EventStartCursorTimeUtc = eventStartCursorTimeUtc;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (SequenceNumber != null)
            {
                return SequenceNumber.ToString();
            }

            if (EventStartCursorTimeUtc != null)
            {
                return EventStartCursorTimeUtc.ToString();
            }

            return "Invalid";
        }
    }
}
