// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.EventHubs;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// Represents event sequence number or a date time used to create an event position in event hub.
    /// </summary>
    public class EventSequencePoint : IEquatable<EventSequencePoint>
    {
        /// <summary>
        /// Sequence number for processing events starting from a given point.
        /// </summary>
        public long? SequenceNumber { get; }

        /// <nodoc />
        public DateTime? EventStartCursorTimeUtc { get; }

        /// <nodoc />
        public EventPosition EventPosition => SequenceNumber != null
            ? EventPosition.FromSequenceNumber(SequenceNumber.Value)
            : EventPosition.FromEnqueuedTime(EventStartCursorTimeUtc.Value);

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
        public bool Equals(EventSequencePoint other)
        {
            return SequenceNumber == other.SequenceNumber && EventStartCursorTimeUtc == other.EventStartCursorTimeUtc;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return EqualityComparer<EventSequencePoint>.Default.Equals(this, obj as EventSequencePoint);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (SequenceNumber, EventStartCursorTimeUtc).GetHashCode();
        }

        /// <nodoc />
        public static bool operator ==(EventSequencePoint left, EventSequencePoint right)
        {
            return Equals(left, right);
        }

        /// <nodoc />
        public static bool operator !=(EventSequencePoint left, EventSequencePoint right)
        {
            return !Equals(left, right);
        }

        /// <nodoc />
        public static EventSequencePoint Parse(string value)
        {
            return new EventSequencePoint(long.Parse(value));
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (SequenceNumber != null)
            {
                return SequenceNumber.ToString();
            }
            else
            {
                return EventStartCursorTimeUtc.ToString();
            }
        }
    }
}
