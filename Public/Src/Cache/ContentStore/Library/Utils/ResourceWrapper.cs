// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Wrapper for a resource within a <see cref="ResourcePool{TKey, TObject}"/>.
    /// </summary>
    /// <typeparam name="TObject">The wrapped type.</typeparam>
    public class ResourceWrapper<TObject>
        where TObject : IStartupShutdownSlim
    {
        private static Tracer Tracer { get; } = new Tracer(nameof(ResourceWrapper<TObject>));

        private readonly object _syncRoot = new object();

        private readonly Guid _id;

        private readonly AsyncLazy<TObject> _lazy;

        private DateTime _lastAccessTime;

        private readonly CancellationTokenSource _shutdownCancellationTokenSource;

        private bool _invalidated;

        private int _referenceCount;

        /// <nodoc />
        internal int ReferenceCount => _referenceCount;

        /// <nodoc />
        internal bool Invalid => _invalidated || (_lazy.IsValueCreated && _lazy.GetValueAsync().IsFaulted);

        internal bool IsValueCreated => _lazy.IsValueCreated;

        /// <nodoc />
        public Task<TObject> LazyValue => _lazy.GetValueAsync();

        /// <nodoc />
        public TObject Value => _lazy.Value;

        /// <nodoc />
        public CancellationToken ShutdownToken => _shutdownCancellationTokenSource.Token;

        /// <summary>
        /// Constructor.
        /// </summary>
        internal ResourceWrapper(Guid id, AsyncLazy<TObject> resource, DateTime now, CancellationToken cancellationToken)
        {
            _id = id;
            _lastAccessTime = now;
            _lazy = resource;
            _shutdownCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        /// <nodoc />
        internal int Acquire(DateTime now)
        {
            lock (_syncRoot)
            {
                _lastAccessTime = now;
                return Interlocked.Increment(ref _referenceCount);
            }
        }

        /// <nodoc />
        internal int Release(DateTime now)
        {
            lock (_syncRoot)
            {
                _lastAccessTime = now;
                return Interlocked.Decrement(ref _referenceCount);
            }
        }

        /// <nodoc />
        internal bool IsAlive(DateTime now, TimeSpan maximumAge)
        {
            return IsAlive(now, maximumAge, out _);
        }

        /// <nodoc />
        internal bool IsAlive(DateTime now, TimeSpan maximumAge, out DateTime lastAccessTime)
        {
            lock (_syncRoot)
            {
                lastAccessTime = _lastAccessTime;
                return !Invalid && now - _lastAccessTime < maximumAge;
            }
        }

        /// <summary>
        /// Invalidates the resource, forcing it to be regenerated on next usage
        /// </summary>
        public virtual void Invalidate(Context context)
        {
            lock (_syncRoot)
            {
                Tracer.Debug(context, $"Invalidating `{nameof(ResourceWrapper<TObject>)}` ({_id}). Previous value is `{_invalidated}`");
                _invalidated = true;
            }
        }

        /// <nodoc />
        internal void CancelOngoingOperations(Context context)
        {
            Tracer.Debug(context, $"Cancelling ongoing operations for `{nameof(ResourceWrapper<TObject>)}` ({_id})");
            _shutdownCancellationTokenSource.Cancel();
        }

        /// <nodoc />
        internal void Dispose(Context context)
        {
            if (_referenceCount != 0)
            {
                // This can only ever happen if an usage of the resource doesn't respect cancellation tokens. In this
                // case, a Dispose of the pool will force a Dispose of the wrapper. We shouldn't fail in this case,
                // because not obeying cancellation is external to the pool.
                Tracer.Warning(context, $"Disposing `{nameof(ResourceWrapper<TObject>)}` ({_id}) which has a reference count of `{_referenceCount}`");
            }

            if (!_shutdownCancellationTokenSource.IsCancellationRequested)
            {
                // This should never happen, and hence signals that a bug occurred. However, it is a bug that we can
                // live with: we just cancel the operations and move on to dispose. Since no other usage of the
                // resource can possibly happen, this is safe.
                Tracer.Error(context, $"Disposing `{nameof(ResourceWrapper<TObject>)}` ({_id}) which hasn't been cancelled");
                _shutdownCancellationTokenSource.Cancel();
            }

            _shutdownCancellationTokenSource.Dispose();
            Tracer.Debug(context, $"Disposed `{nameof(ResourceWrapper<TObject>)}` ({_id})");
        }
    }
}
