// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Pips;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Defines the information needed for caching of a pip.
    /// NOTE: No behavior should be defined in this class
    /// </summary>
    public class RunnablePipObserver
    {
        /// <summary>
        /// Gets the default no-op runnable pip observer
        /// </summary>
        public static readonly RunnablePipObserver Default = new RunnablePipObserver();

        /// <summary>
        /// Gets the logging activity id for the given pip (if specified)
        /// </summary>
        public virtual Guid? GetActivityId(PipId pipId)
        {
            return null;
        }

        /// <summary>
        /// Notification that the given runnable pip has started a particular step
        /// </summary>
        public virtual void StartStep(RunnablePip runnablePip, PipExecutionStep step)
        {
        }

        /// <summary>
        /// Notification that the given runnable pip has ended the pip step with the duration taken by that step
        /// </summary>
        public virtual void EndStep(RunnablePip runnablePip, PipExecutionStep step, TimeSpan duration)
        {
        }
    }
}
