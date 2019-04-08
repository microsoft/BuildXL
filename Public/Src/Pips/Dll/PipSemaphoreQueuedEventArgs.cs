// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Pips
{
    /// <summary>
    /// Arguments for event handler that gets invoked when a pip has been queued or dequeued because of semaphore constraints
    /// </summary>
    public sealed class PipSemaphoreQueuedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates an instance
        /// </summary>
        public PipSemaphoreQueuedEventArgs(bool queued, PipId pipId)
        {
            Queued = queued;
            PipId = pipId;
        }

        /// <summary>
        /// Whether the item was queued (otherwise, it got dequeued).
        /// </summary>
        public bool Queued { get; private set; }

        /// <summary>
        /// The Pip id
        /// </summary>
        public PipId PipId { get; private set; }
    }
}
