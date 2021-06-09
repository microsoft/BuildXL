// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using Grpc.Core;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Grpc.Configuration;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public abstract class StartupShutdownComponentBase : StartupShutdownSlimBase
    {
        private readonly List<IStartupShutdownSlim> _nestedComponents = new List<IStartupShutdownSlim>();

        public void LinkLifetime(IStartupShutdownSlim nestedComponent)
        {
            Contract.Requires(!StartupStarted, "Nested components must be linked before startup");
            _nestedComponents.Add(nestedComponent);
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            foreach (var nestedComponent in _nestedComponents)
            {
                await nestedComponent.StartupAsync(context).ThrowIfFailureAsync();
            }

            return await base.StartupCoreAsync(context);
        }

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var success = await base.ShutdownCoreAsync(context);

            foreach (var nestedComponent in _nestedComponents)
            {
                success &= await nestedComponent.ShutdownAsync(context);
            }

            return success;
        }
    }
}
