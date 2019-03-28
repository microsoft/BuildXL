// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Arguments for event handler that gets invoked when an item has been drained from the queue and run
    /// </summary>
    public sealed class ItemCompletedEventArgs<TItem, TTask> : EventArgs
        where TTask : Task
    {
        /// <summary>
        /// Creates an instance
        /// </summary>
        public ItemCompletedEventArgs(TItem item, TTask task)
        {
            Contract.Requires(task != null);
            Contract.Requires(task.IsCompleted);
            Item = item;
            Task = task;
            IsExceptionHandled = false;
        }

        /// <summary>
        /// The value from which the task was created
        /// </summary>
        public TItem Item { get; private set; }

        /// <summary>
        /// The completed task
        /// </summary>
        /// <remarks>
        /// The task may be <code>null</code> if the task creator returned a <code>null</code>; otherwise, the task has completed (
        /// <code>Status</code> is one of the following: Canceled, Faulted, RunToCompletion)
        /// </remarks>
        public TTask Task { get; private set; }

        /// <summary>
        /// Indicates if the exception is handled or not inside the event handler.
        /// </summary>
        public bool IsExceptionHandled { get; set; }
    }
}
