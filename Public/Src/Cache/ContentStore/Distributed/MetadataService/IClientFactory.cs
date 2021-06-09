// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Obtains a connection to a host that implements <typeparamref name="TClient"/>
    /// </summary>
    public interface IClientFactory<TClient> : IStartupShutdownSlim
    {
        ValueTask<TClient> CreateClientAsync(OperationContext context);
    }
}
