// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Scheduler.WorkDispatcher
{
    /// <summary>
    /// Dispatcher releaser
    /// </summary>
    public class DispatcherReleaser
    {
        private DispatcherQueue m_queue;

        /// <nodoc/>
        public DispatcherReleaser(DispatcherQueue queue)
        {
            m_queue = queue;
        }

        /// <summary>
        /// Release the dispatcher if it is not released before.
        /// </summary>
        /// <remarks>
        /// Not thread safe 
        /// </remarks>
        public bool Release()
        {
            if (m_queue == null)
            {
                return false;
            }

            m_queue.ReleaseResource();
            m_queue = null;
            return true;
        }
    }
}
