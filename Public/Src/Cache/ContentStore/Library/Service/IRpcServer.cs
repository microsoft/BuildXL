// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Stores;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// Interface for RPC servers managed by the content store service.
    /// </summary>
    internal interface IRpcServer : IStartupShutdownSlim
    {
    }
}
