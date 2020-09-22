// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Wrapper for a resource within a <see cref="ResourcePoolV2{TKey, TObject}"/>.
    /// </summary>
    /// <typeparam name="TObject">The wrapped type.</typeparam>
    public sealed class ResourceWrapperV2<TObject> : IDisposable
        where TObject : IStartupShutdownSlim
    {
        /// <nodoc />
        public DateTime LastAccessTime;

        private bool _invalidated;

        /// <nodoc />
        public bool Invalid => _invalidated || (_lazy.IsValueCreated && _lazy.GetValueAsync().IsFaulted);

        private readonly AsyncLazy<TObject> _lazy;

        /// <nodoc />
        public Task<TObject> LazyValue => _lazy.GetValueAsync();

        /// <nodoc />
        public TObject Value => _lazy.Value;

        /// <nodoc />
        public CancellationTokenSource ShutdownCancellationTokenSource { get; }

        /// <nodoc />
        public CancellationToken ShutdownToken => ShutdownCancellationTokenSource.Token;

        /// <nodoc />
        public int ReferenceCount;

        /// <summary>
        /// Constructor.
        /// </summary>
        public ResourceWrapperV2(AsyncLazy<TObject> resource, DateTime now, CancellationToken cancellationToken)
        {
            LastAccessTime = now;
            _lazy = resource;
            ShutdownCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        /// <summary>
        /// Invalidates the resource, forcing it to be regenerated on next usage
        /// </summary>
        public void Invalidate()
        {
            lock (this)
            {
                _invalidated = true;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Contract.Assert(ReferenceCount == 0);
            Contract.Assert(ShutdownCancellationTokenSource.IsCancellationRequested);
            ShutdownCancellationTokenSource.Dispose();
        }
    }
}
