// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Scheduler.WorkDispatcher
{
    /// <summary>
    /// Dispatcher releaser
    /// </summary>
    public class DispatcherReleaser : IDisposable
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

        /// <nodoc/>
        public void Dispose()
        {
            Release();
        }
    }
}
