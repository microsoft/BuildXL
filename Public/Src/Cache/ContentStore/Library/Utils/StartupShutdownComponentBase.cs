// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Tasks;

#nullable disable

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <todoc />
    public abstract class StartupShutdownComponentBase : StartupShutdownBase
    {
        private readonly List<IStartupShutdownSlim> _nestedComponents = new List<IStartupShutdownSlim>();

        protected ILogger StartupLogger { get; private set; }
        protected Context StartupContext { get; private set; }

        /// <todoc />
        public void LinkLifetime(IStartupShutdownSlim nestedComponent)
        {
            Contract.Requires(!StartupStarted, "Nested components must be linked before startup");
            if (nestedComponent != null)
            {
                _nestedComponents.Add(nestedComponent);
            }
        }

        /// <summary>
        /// Runs the requested operation in background.
        /// NOTE: Must be called before Startup.
        /// </summary>
        protected void RunInBackground(string operationName, Func<OperationContext, Task<BoolResult>> operation, bool fireAndForget = false)
        {
            LinkLifetime(new BackgroundOperation($"{Tracer.Name}.{operationName}", fireAndForget
                ? c => operation(c).FireAndForgetErrorsAsync(c, operationName).WithResultAsync(BoolResult.Success)
                : operation));
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            StartupContext = context;
            StartupLogger = context.TracingContext.Logger;

            // Startup dependent components
            // Background operations need to run after startup is finished
            foreach (var nestedComponent in _nestedComponents.Where(n => n is not BackgroundOperation))
            {
                await nestedComponent.StartupAsync(context).ThrowIfFailureAsync();
            }

            await base.StartupCoreAsync(context).ThrowIfFailureAsync();

            // Background operations need to run after startup is finished
            foreach (var nestedComponent in _nestedComponents.Where(n => n is BackgroundOperation))
            {
                await nestedComponent.StartupAsync(context).ThrowIfFailureAsync();
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var success = BoolResult.Success;

            // Background operations need to stop before shutting down the component
            foreach (var nestedComponent in _nestedComponents.Where(n => n is BackgroundOperation))
            {
                success &= await nestedComponent.ShutdownAsync(context);
            }

            success &= await base.ShutdownCoreAsync(context);

            foreach (var nestedComponent in _nestedComponents.Where(n => n is not BackgroundOperation))
            {
                success &= await nestedComponent.ShutdownAsync(context);
            }

            return success;
        }
    }
}
