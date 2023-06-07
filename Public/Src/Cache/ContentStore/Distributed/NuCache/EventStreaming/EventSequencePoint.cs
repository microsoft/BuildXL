// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json.Serialization;
using Azure.Messaging.EventHubs.Consumer;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// Represents event sequence number or a date time used to create an event position in event hub.
    /// </summary>
    public record EventSequencePoint
    {
        public static EventSequencePoint Invalid { get; } = new();

        /// <summary>
        /// Sequence number for processing events starting from a given index.
        /// </summary>
        public long? SequenceNumber { get; init; }

        /// <summary>
        /// Timestamp for processing events starting from a given point in time.
        /// </summary>
        public DateTime? EventStartCursorTimeUtc { get; init; }

        /// <summary>
        /// This is a cursor that doesn't map to either a <see cref="SequenceNumber"/> or a
        /// <see cref="EventStartCursorTimeUtc"/>.
        /// </summary>
        public string? Cursor { get; init; }

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

        /// <nodoc />
        public EventSequencePoint(string cursor)
        {
            Cursor = cursor;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (SequenceNumber is not null)
            {
                return SequenceNumber.Value.ToString();
            }

            if (EventStartCursorTimeUtc is not null)
            {
                return EventStartCursorTimeUtc.Value.ToString();
            }

            return Cursor ?? "Invalid";
        }
    }
}
