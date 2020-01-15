// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Native.Processes
{
    /// <nodoc />
    public interface IProcessInjector : IDisposable
    {
        /// <nodoc />
        bool IsDisposed { get; }

        /// <nodoc />
        IntPtr Injector();

        /// <nodoc />
        uint Inject(uint processId, bool inheritedHandles);
    }
}
