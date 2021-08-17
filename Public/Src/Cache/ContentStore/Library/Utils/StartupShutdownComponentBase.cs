// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

#nullable disable

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <todoc />
    public abstract class StartupShutdownComponentBase : StartupShutdownSlimBase
    {
        private readonly List<IStartupShutdownSlim> _nestedComponents = new List<IStartupShutdownSlim>();

        protected ILogger StartupLogger { get; private set; }
        protected Context StartupContext { get; private set; }

        /// <todoc />
        public void LinkLifetime(IStartupShutdownSlim nestedComponent)
        {
            Contract.Requires(!StartupStarted, "Nested components must be linked before startup");
            Contract.RequiresNotNull(nestedComponent);
            _nestedComponents.Add(nestedComponent);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            StartupContext = context;
            StartupLogger = context.TracingContext.Logger;

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
