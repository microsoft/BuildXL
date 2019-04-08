// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using BuildXL.Cache.ContentStore.Interfaces.Stores;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Base implementation of <see cref="IStartupShutdown"/> interface.
    /// </summary>
    public abstract class StartupShutdownBase : StartupShutdownSlimBase, IStartupShutdown
    {
        /// <summary>
        /// True when the instance is already disposed.
        /// </summary>
        protected bool Disposed { get; private set; }

        /// <nodoc />
        protected bool DisposedOrShutdownStarted => Disposed || ShutdownStarted;

        /// <nodoc />
        public void Dispose()
        {
            if (Disposed)
            {
                return;
            }

            TriggerShutdownStarted();

            DisposeCore();

            Disposed = true;
        }

        /// <summary>
        /// Shorter implementation of Dispose pattern that does not support finalization.
        /// </summary>
        protected virtual void DisposeCore()
        {
        }

        /// <nodoc />
        protected override void ThrowIfInvalid([CallerMemberName]string operation = null)
        {
            base.ThrowIfInvalid(operation);

            if (Disposed)
            {
                throw new InvalidOperationException($"The component {Tracer.Name} is disposed for '{operation}'.");
            }
        }
    }

}
