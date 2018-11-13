// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Represents the information contained in the header of an execution event
    /// </summary>
    class EventHeader
    {
        /// <summary>
        /// The event id
        /// </summary>
        public UInt32 EventId;

        /// <summary>
        /// The id of the worker the event originated on
        /// </summary>
        public UInt32 WorkerId;

        /// <summary>
        /// The event timestamp
        /// </summary>
        public Int64 Timestamp;

        /// <summary>
        /// The event pay load size
        /// </summary>
        public Int32 EventPayloadSize;

        /// <summary>
        /// Reads and returns an <see cref="EventHeader"/> given a <see cref="BuildXLReader"/>. 
        /// It is the callers responsibility to ensure the reader's initial position is set correctly.
        /// </summary>
        public static EventHeader ReadFrom(BuildXLReader reader)
        {
            var eventHeader = new EventHeader();
            // Order matters!
            eventHeader.EventId = reader.ReadUInt32Compact();
            eventHeader.WorkerId = reader.ReadUInt32Compact();
            eventHeader.Timestamp = reader.ReadInt64Compact();
            eventHeader.EventPayloadSize = reader.ReadInt32Compact();

            return eventHeader;
        }
    }
}
