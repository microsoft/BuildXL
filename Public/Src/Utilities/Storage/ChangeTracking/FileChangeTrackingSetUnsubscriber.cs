// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Class for unsubscribing from change tracker.
    /// </summary>
    internal class FileChangeTrackingSetUnsubscriber<T> : IDisposable
    {
        /// <summary>
        /// List of known observers.
        /// </summary>
        private readonly List<IObserver<T>> m_observers;

        /// <summary>
        /// The observer.
        /// </summary>
        public readonly IObserver<T> Observer;

        /// <summary>
        /// Creates an instance of <see cref="FileChangeTrackingSetUnsubscriber{T}" />
        /// </summary>
        public FileChangeTrackingSetUnsubscriber(List<IObserver<T>> observers, IObserver<T> observer)
        {
            Contract.Requires(observers != null);
            Contract.Requires(observer != null);

            m_observers = observers;
            Observer = observer;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            m_observers.Remove(Observer);
        }
    }
}
