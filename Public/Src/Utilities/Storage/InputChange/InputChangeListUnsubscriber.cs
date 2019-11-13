// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Storage.ChangeTracking;

namespace BuildXL.Storage.InputChange
{
    /// <summary>
    /// Unsubscriber of <see cref="InputChangeList"/>.
    /// </summary>
    public sealed class InputChangeListUnsubscriber : IDisposable
    {
        /// <summary>
        /// List of known observers.
        /// </summary>
        private readonly List<IObserver<ChangedPathInfo>> m_observers;

        /// <summary>
        /// The observer.
        /// </summary>
        public readonly IObserver<ChangedPathInfo> Observer;

        /// <summary>
        /// Creates an instance of <see cref="InputChangeListUnsubscriber" />
        /// </summary>
        public InputChangeListUnsubscriber(List<IObserver<ChangedPathInfo>> observers, IObserver<ChangedPathInfo> observer)
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
