// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Represents a connection to a local instance of <see cref="TClient"/>
    /// </summary>
    public class LocalClient<TClient> : StartupShutdownComponentBase
    {
        private readonly Lazy<TClient> _lazyClient;
        public TClient Client => _lazyClient.Value;

        public MachineLocation Location { get; }

        protected override Tracer Tracer { get; } = new Tracer(nameof(LocalClient<TClient>));

        public override bool AllowMultipleStartupAndShutdowns => true;

        public LocalClient(MachineLocation location, TClient client)
            : this(location, () => client)
        {
        }

        public LocalClient(MachineLocation location, Func<TClient> clientFactory)
        {
            Location = location;
            _lazyClient = new Lazy<TClient>(clientFactory);
        }

        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            if (Client is IStartupShutdownSlim component)
            {
                return component.StartupAsync(context);
            }

            return base.StartupCoreAsync(context);
        }

        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            if (Client is IStartupShutdownSlim component)
            {
                return component.ShutdownAsync(context);
            }

            return base.ShutdownCoreAsync(context);
        }

    }
}
