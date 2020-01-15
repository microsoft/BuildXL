// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// Defines a queue in which log actions can be enqueued for asynchronous logging 
    /// </summary>
    public interface ILoggingQueue
    {
        /// <summary>
        /// Enqueues an asynchronous log action for the given event
        /// </summary>
        void EnqueueLogAction(int eventId, Action logAction, string eventName);
    }
}
