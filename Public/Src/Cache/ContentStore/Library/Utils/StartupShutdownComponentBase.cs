// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <todoc />
    public abstract class StartupShutdownComponentBase : StartupShutdownSlimBase
    {
        private readonly List<IStartupShutdownSlim> _nestedComponents = new List<IStartupShutdownSlim>();

        /// <todoc />
        public void LinkLifetime(IStartupShutdownSlim nestedComponent)
        {
            Contract.Requires(!StartupStarted, "Nested components must be linked before startup");
            _nestedComponents.Add(nestedComponent);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            foreach (var nestedComponent in _nestedComponents)
            {
                await nestedComponent.StartupAsync(context).ThrowIfFailureAsync();
            }

            return await base.StartupCoreAsync(context);
        }

        /// <inheritdoc />
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
