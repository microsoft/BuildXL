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
        /// <summary>
        /// The worker name
        /// </summary>
        public string WorkerName;

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
    }
}