// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// Represents an event forwarded from a worker
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct WorkerForwardedEvent
    {
        // ========================== IMPORTANT ================================
        //  Do not change the order of the fields below. 
        //  We rely on the indices of these fields in several places,
        //  when unpacking the payload of forwarded log events.
        //  The PipProcessEventFields constructor is also aware of
        //  the position of the PipProcessEvent field below (5th in this struct)
        // =====================================================================

        /// <summary>
        /// The message of the worker event
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// The ID of the original event
        /// </summary>
        public int EventId { get; set; }

        /// <summary>
        /// The name of the original event
        /// </summary>
        public string EventName { get; set; }

        /// <summary>
        /// The keywords of the original event
        /// </summary>
        public long EventKeywords { get; set; }

        /// <summary>
        /// The worker name
        /// </summary>
        public string WorkerName { get; set; }

        /// <summary>
        /// The original PipProcessError/PipProcessWarning event
        /// </summary>
        public PipProcessEventFields PipProcessEvent { get; set; }
    }
}