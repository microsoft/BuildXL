// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.CompilerServices;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Base implementation of <see cref="IDisposable"/> interface.
    /// </summary>
    public abstract class Disposable : IDisposable
    {
        /// <summary>
        /// True when the instance is already disposed.
        /// </summary>
        protected bool Disposed { get; private set; }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Disposed)
            {
                return;
            }

            DisposeCore();

            Disposed = true;
        }

        /// <summary>
        /// Shorter implementation of Dispose pattern that does not support finalization.
        /// </summary>
        protected virtual void DisposeCore() { }


        /// <nodoc />
        protected void ThrowIfInvalid([CallerMemberName]string operation = null)
        {
            if (Disposed)
            {
                throw new InvalidOperationException($"The component {GetType()} is disposed for '{operation}'.");
            }
        }
    }
}
