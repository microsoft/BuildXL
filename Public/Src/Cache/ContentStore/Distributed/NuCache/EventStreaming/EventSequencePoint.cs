// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using Microsoft.Azure.EventHubs;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// Represents event sequence number or a date time used to create an event position in event hub.
    /// </summary>
    /// <remarks>
    /// This is not a record because .NET Core 3.1 does not support specifying constructors.
    /// 
    /// See: https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-immutability?pivots=dotnet-5-0
    /// </remarks>
    public class EventSequencePoint : IEquatable<EventSequencePoint>
    {
        public static EventSequencePoint Invalid { get; } = new EventSequencePoint();

        /// <summary>
        /// Sequence number for processing events starting from a given point.
        /// </summary>
        public long? SequenceNumber { get; set; }

        /// <nodoc />
        public DateTime? EventStartCursorTimeUtc { get; set; }

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
        public bool Equals(EventSequencePoint other)
        {
            if (other is null)
            {
                return false;
            }

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
            else if (EventStartCursorTimeUtc != null)
            {
                return EventStartCursorTimeUtc.ToString();
            }
            else
            {
                return "Invalid";
            }
        }
    }
}
