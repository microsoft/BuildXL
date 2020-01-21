// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Wrapper for a resource within a <see cref="ResourcePool{TKey, TObject}"/>.
    /// </summary>
    /// <typeparam name="TObject">The wrapped type.</typeparam>
    public sealed class ResourceWrapper<TObject> : IDisposable where TObject : IStartupShutdownSlim
    {
        internal DateTime LastUseTime;
        private int _uses;
        internal readonly Lazy<TObject> Resource;

        /// <summary>
        /// Count of ongoing uses of this resource.
        /// </summary>
        public int Uses => _uses;

        /// <summary>
        /// Whether the resource's underlying lazy wrapper has been evaluated yet.
        /// </summary>
        internal bool IsValueCreated => Resource.IsValueCreated;

        /// <summary>
        /// The contained resource.
        /// </summary>
        public TObject Value => Resource.Value;

        /// <summary>
        /// Constructor.
        /// </summary>
        public ResourceWrapper(Func<TObject> resourceFactory, Context context)
        {
            LastUseTime = DateTime.MinValue;
            Resource = new Lazy<TObject>(() => {
                var resource = resourceFactory();
                var result = resource.StartupAsync(context).ThrowIfFailure().GetAwaiter().GetResult();
                return resource;
            });
        }

        /// <summary>
        /// Attempt to reserve the resource. Fails if marked for shutdown.
        /// </summary>
        /// <param name="reused">Whether the resource has been used previously.</param>
        /// <param name="clock">Clock to use</param>
        /// <returns>Whether the resource is approved for use.</returns>
        public bool TryAcquire(out bool reused, IClock clock)
        {
            lock (this)
            {
                if (Resource.IsValueCreated && Resource.Value.ShutdownStarted)
                {
                    throw Contract.AssertFailure($"Found resource which has already begun shutdown");
                }

                _uses++;

                reused = LastUseTime != DateTime.MinValue;
                if (_uses > 0)
                {
                    LastUseTime = clock.UtcNow;
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
                if (_uses == 0 && (force || LastUseTime <= earliestLastUseTime))
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
