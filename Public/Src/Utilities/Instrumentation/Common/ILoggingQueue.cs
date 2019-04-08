// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
