// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Class for unsubscribing from multi volumes change tracking.
    /// </summary>
    internal class MultiVolumesChangeTrackingUnsubscriber<T> : IDisposable
    {
        private readonly List<KeyValuePair<ulong, IDisposable>> m_perVolumeUnsubscribers;

        /// <summary>
        /// The observer.
        /// </summary>
        public readonly IObserver<T> Observer;

        /// <summary>
        /// Per-volume unsubscribers.
        /// </summary>
        public IEnumerable<KeyValuePair<ulong, IDisposable>> PerVolumeUnsubscribers => m_perVolumeUnsubscribers;

        /// <summary>
        /// Creates an instance of <see cref="MultiVolumesChangeTrackingUnsubscriber{T}" />
        /// </summary>
        public MultiVolumesChangeTrackingUnsubscriber(IObserver<T> observer, List<KeyValuePair<ulong, IDisposable>> perVolumeUnsubscribers)
        {
            Contract.Requires(observer != null);
            Contract.Requires(perVolumeUnsubscribers != null);

            Observer = observer;
            m_perVolumeUnsubscribers = perVolumeUnsubscribers;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (var unsubscriber in m_perVolumeUnsubscribers)
            {
                unsubscriber.Value.Dispose();
            }

            m_perVolumeUnsubscribers.Clear();
        }
    }
}
