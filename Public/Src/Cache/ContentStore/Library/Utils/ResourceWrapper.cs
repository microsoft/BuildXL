// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Wrapper for a resource within a <see cref="ResourcePool{TKey, TObject}"/>.
    /// </summary>
    /// <typeparam name="TObject">The wrapped type.</typeparam>
    public sealed class ResourceWrapper<TObject> : IDisposable where TObject : IShutdown<BoolResult>
    {
        internal DateTime _lastUseTime;
        private int _uses;
        internal readonly Lazy<TObject> _resource;

        /// <summary>
        /// Count of ongoing uses of this resource.
        /// </summary>
        public int Uses
        {
            get
            {
                return _uses;
            }
        }

        /// <summary>
        /// Whether the resource's underlying lazy wrapper has been evaluated yet.
        /// </summary>
        internal bool IsValueCreated
        {
            get
            {
                return _resource.IsValueCreated;
            }
        }

        /// <summary>
        /// The contained resource.
        /// </summary>
        public TObject Value
        {
            get
            {
                return _resource.Value;
            }
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public ResourceWrapper(Func<TObject> resourceFactory)
        {
            _lastUseTime = DateTime.MinValue;
            _resource = new Lazy<TObject>(resourceFactory);
        }

        /// <summary>
        /// Attempt to reserve the resource. Fails if marked for shutdown.
        /// </summary>
        /// <param name="reused">Whether the resource has been used previously.</param>
        /// <returns>Whether the resource is approved for use.</returns>
        public bool TryAcquire(out bool reused)
        {
            lock (this)
            {
                if (_resource.IsValueCreated && _resource.Value.ShutdownStarted)
                {
                    throw Contract.AssertFailure($"Found resource which has already begun shutdown");
                }

                _uses++;

                reused = _lastUseTime != DateTime.MinValue;
                if (_uses > 0)
                {
                    _lastUseTime = DateTime.UtcNow;
                    return true;
                }
            }

            reused = false;
            return false;
        }

        /// <summary>
        /// Attempt to prepare the resource for shutdown, based on current uses and last use time.
        /// </summary>
        /// <param name="force">Whether last use time should be ignored.</param>
        /// <param name="earliestLastUseTime">If the resource has been used since this time, then it is available for shutdown.</param>
        /// <returns>Whether the resource can be marked for shutdown.</returns>
        public bool TryMarkForShutdown(bool force, DateTime earliestLastUseTime)
        {
            lock (this)
            {
                if (_uses == 0 && (force || _lastUseTime < earliestLastUseTime))
                {
                    _uses = int.MinValue;
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (this)
            {
                _uses--;
            }
        }
    }
}
