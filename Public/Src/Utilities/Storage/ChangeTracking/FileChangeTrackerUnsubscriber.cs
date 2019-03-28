// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Class for unsubscribing <see cref="FileChangeProcessor" />.
    /// </summary>
    public class FileChangeTrackerUnsubscriber : IDisposable
    {
        /// <summary>
        /// The observer.
        /// </summary>
        public readonly IFileChangeTrackingObserver Observer;

        private readonly List<IFileChangeTrackingObserver> m_observers;
        private readonly List<IDisposable> m_unsubscribers;

        /// <summary>
        /// Creates an instance of <see cref="FileChangeTrackerUnsubscriber"/>.
        /// </summary>
        public FileChangeTrackerUnsubscriber(
            List<IFileChangeTrackingObserver> observers,
            IFileChangeTrackingObserver observer,
            List<IDisposable> unsubscribers)
        {
            Contract.Requires(observers != null);
            Contract.Requires(observer != null);
            Contract.Requires(unsubscribers != null);

            m_observers = observers;
            Observer = observer;
            m_unsubscribers = unsubscribers;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (var unsubscriber in m_unsubscribers)
            {
                unsubscriber.Dispose();
            }

            m_observers.Remove(Observer);
        }
    }
}
