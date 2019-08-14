// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using JetBrains.Annotations;

namespace Tool.ServicePipDaemon
{
    /// <summary>
    /// Utility class for synchronizing request to "reload" (reconstruct) some underlying value.
    /// This class tags every created value with a version, and ensures that multiple requests
    /// to reload the same version result in a single creation of a new value.
    /// </summary>
    /// <typeparam name="T">Type of the underlying value</typeparam>
    public sealed class Reloader<T> : IDisposable
    {
        /// <summary>A tuple containing a value and its version.</summary>
        public sealed class VersionedValue
        {
            /// <nodoc />
            public int Version { get; }

            /// <nodoc />
            public T Value { get; }

            /// <nodoc />
            internal VersionedValue(int version, T instance)
            {
                Version = version;
                Value = instance;
            }
        }

        private readonly object m_lock;
        private readonly Func<T> m_constructor;
        private readonly Action<T> m_destructor;
        private readonly Stack<VersionedValue> m_stackOfValues;

        /// <summary>Current version.</summary>
        public int CurrentVersion => m_stackOfValues.Count;

        /// <summary>Current versioned value or <code>null</code> if <see cref="Reload(int)"/> or <see cref="EnsureLoaded"/> has not been called yet.</summary>
        [CanBeNull]
        public VersionedValue CurrentVersionedValue => m_stackOfValues.Any() ? m_stackOfValues.Peek() : null;

        /// <summary>Constructor.</summary>
        public Reloader(Func<T> constructor, Action<T> destructor = null)
        {
            m_lock = new object();
            m_constructor = constructor;
            m_destructor = destructor ?? new Action<T>(t => { });
            m_stackOfValues = new Stack<VersionedValue>();
        }

        /// <summary>
        /// Ensure the initial value is loaded
        /// </summary>
        public void EnsureLoaded()
        {
            // Ensure value initialized of access
            if (CurrentVersion == 0)
            {
                Reload(0);
            }
        }

        /// <summary>
        /// If the given version is equal to the current version, creates a new value;
        /// returns whether a new value was created.
        /// </summary>
        public bool Reload(int version)
        {
            Contract.Requires(version <= CurrentVersion);

            lock (m_lock)
            {
                if (version == CurrentVersion)
                {
                    CreateAndPushNewInstance();
                    return true;
                }

                return false;
            }
        }

        /// <summary>Calls destructor (if provided) on all created values.</summary>
        public void Dispose()
        {
            foreach (var elem in m_stackOfValues)
            {
                m_destructor(elem.Value);
            }
        }

        private VersionedValue CreateAndPushNewInstance()
        {
            Contract.Ensures(Contract.Result<VersionedValue>().Version == CurrentVersion);

            var instance = new VersionedValue(m_stackOfValues.Count + 1, m_constructor());
            m_stackOfValues.Push(instance);
            return instance;
        }
    }
}
