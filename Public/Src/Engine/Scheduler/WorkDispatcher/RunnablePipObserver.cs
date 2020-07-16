// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        public virtual Guid? GetActivityId(RunnablePip runnablePip)
        {
            return null;
        }

        /// <summary>
        /// Notification that the given runnable pip has started a particular step
        /// </summary>
        public virtual void StartStep(RunnablePip runnablePip)
        {
        }

        /// <summary>
        /// Notification that the given runnable pip has ended the pip step with the duration taken by that step
        /// </summary>
        public virtual void EndStep(RunnablePip runnablePip)
        {
        }
    }
}
