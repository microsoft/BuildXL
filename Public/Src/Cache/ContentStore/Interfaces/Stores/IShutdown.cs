// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

#nullable disable

namespace BuildXL.Cache.ContentStore.Interfaces.Stores
{
    /// <summary>
    ///     To avoid relying on synchronous IDisposable to block waiting for background threads
    ///     to complete (which can deadlock in some cases), we make this explicit and require
    ///     clients to shutdown services before Dispose.
    /// </summary>
    public interface IShutdown<T> : IShutdownSlim<T>, IDisposable
    {
    }
}
