// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Arguments for event handler that gets invoked when an item has been queued or dequeued because of semaphore constraints
    /// </summary>
    public sealed class ItemSemaphoreQueuedEventArgs<TItem> : EventArgs
    {
        /// <summary>
        /// Creates an instance
        /// </summary>
        public ItemSemaphoreQueuedEventArgs(bool queued, TItem item, ItemResources itemResources)
        {
            Queued = queued;
            Item = item;
            ItemResources = itemResources;
        }

        /// <summary>
        /// Whether the item was queued (otherwise, it got dequeued).
        /// </summary>
        public bool Queued { get; private set; }

        /// <summary>
        /// The item
        /// </summary>
        public TItem Item { get; private set; }

        /// <summary>
        /// The item resources
        /// </summary>
        public ItemResources ItemResources { get; private set; }
    }
}
