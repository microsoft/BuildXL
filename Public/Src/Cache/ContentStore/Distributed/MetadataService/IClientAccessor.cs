// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Obtains a connection to a host that implements <typeparamref name="TService"/>
    /// </summary>
    public interface IClientAccessor<TKey, TService> : IStartupShutdownSlim
    {
        Task<TResult> UseAsync<TResult>(OperationContext context, TKey key, Func<TService, Task<TResult>> operation);
    }

    /// <summary>
    /// Obtains a connection to a host that implements <typeparamref name="TService"/>
    /// </summary>
    public interface IClientAccessor<TService> : IStartupShutdownSlim
    {
        Task<TResult> UseAsync<TResult>(OperationContext context, Func<TService, Task<TResult>> operation);
    }

    public class FixedClientAccessor<TService> : StartupShutdownComponentBase, IClientAccessor<TService>
    {
        protected override Tracer Tracer { get; } = new(nameof(FixedClientAccessor<TService>));

        private readonly TService _service;

        public FixedClientAccessor(TService service)
        {
            _service = service;

            if (_service is IStartupShutdownSlim startupShutdownSlim)
            {
                LinkLifetime(startupShutdownSlim);
            }
        }

        public Task<TResult> UseAsync<TResult>(OperationContext context, Func<TService, Task<TResult>> operation)
        {
            return operation?.Invoke(_service);
        }
    }
}
