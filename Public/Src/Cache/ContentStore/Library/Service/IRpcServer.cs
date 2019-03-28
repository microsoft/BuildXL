// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
