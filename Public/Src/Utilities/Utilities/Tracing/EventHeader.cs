// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Represents the information contained in the header of an execution event
    /// </summary>
    /// <param name="EventId">The event id</param>
    /// <param name="WorkerId">The id of the worker the event originated on</param>
    /// <param name="Timestamp">The event timestamp</param>
    /// <param name="EventPayloadSize">The event payload size</param>
    internal readonly record struct EventHeader(
        uint EventId,
        uint WorkerId,
        long Timestamp,
        int EventPayloadSize)
    {
        /// <summary>
        /// Reads and returns an <see cref="EventHeader"/> given a <see cref="BuildXLReader"/>. 
        /// It is the callers responsibility to ensure the reader's initial position is set correctly.
        /// </summary>
        public static EventHeader ReadFrom(BuildXLReader reader)
        {
            // Order matters!
            var eventId = reader.ReadUInt32Compact();
            var workerId = reader.ReadUInt32Compact();
            var timestamp = reader.ReadInt64Compact();
            var eventPayloadSize = reader.ReadInt32Compact();

            return new EventHeader(eventId, workerId, timestamp, eventPayloadSize);
        }
    }
}
