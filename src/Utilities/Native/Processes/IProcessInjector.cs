// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
