// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

#nullable enable

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Class that manages the lifetime of sub-components.
    /// </summary>
    public abstract class StartupShutdownComponentBase : StartupShutdownBase
    {
        /// <inheritdoc />
        public override bool AllowMultipleStartupAndShutdowns => true;

        /// <summary>
        /// A list of nested components eligible for shutdown
        /// (i.e. the components for which StartupAsync method was called regardless of the result of that call).
        /// </summary>
        private readonly Stack<IStartupShutdownSlim> _shutdownEligibleComponents = new();
        private readonly List<IStartupShutdownSlim> _nestedComponents = new();

        [MemberNotNullWhen(true, nameof(StartupLogger), nameof(StartupContext))]
        public override bool StartupCompleted => base.StartupCompleted;

        protected ILogger? StartupLogger { get; private set; }
        protected Context? StartupContext { get; private set; }

        /// <summary>
        /// Notify that the <paramref name="nestedComponent"/>'s lifetime is owned by the current instance.
        /// NOTE: Must be called before Startup.
        /// </summary>
        public void LinkLifetime(IStartupShutdownSlim? nestedComponent)
        {
            Contract.Requires(!StartupStarted, "Nested components must be linked before startup.");
            if (nestedComponent != null)
            {
                // Some components do support multiple calls to Startup/Shutdown, so we can't assert
                // that the nested component is not started.
                _nestedComponents.Add(nestedComponent);
            }
        }

        /// <inheritdoc cref="LinkLifetime" />
        public T Link<T>(T nestedComponent)
            where T : IStartupShutdownSlim
        {
            LinkLifetime(nestedComponent);
            return nestedComponent;
        }

        /// <summary>
        /// Runs the requested operation in background.
        /// NOTE: Must be called before Startup.
        /// </summary>
        protected void RunInBackground(string operationName, Func<OperationContext, Task<BoolResult>> operation, bool fireAndForget = false)
        {
            Contract.Requires(!StartupStarted, "The method must be called before startup.");
            LinkLifetime(new BackgroundOperation($"{Tracer.Name}.{operationName}", fireAndForget
                ? c => operation(c).FireAndForgetErrorsAsync(c, operationName).WithResultAsync(BoolResult.Success)
                : operation));
        }

        /// <inheritdoc />
        protected sealed override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            StartupContext = context;
            StartupLogger = context.TracingContext.Logger;

            // Startup dependent components
            // Background operations need to run after startup is finished
            foreach (var nestedComponent in _nestedComponents.Where(n => n is not BackgroundOperation))
            {
                _shutdownEligibleComponents.Push(nestedComponent);
                await nestedComponent.StartupAsync(context).ThrowIfFailureAsync();
            }

            await StartupComponentAsync(context).ThrowIfFailureAsync();

            // Background operations need to run after startup is finished
            foreach (var nestedComponent in _nestedComponents.Where(n => n is BackgroundOperation))
            {
                _shutdownEligibleComponents.Push(nestedComponent);
                await nestedComponent.StartupAsync(context).ThrowIfFailureAsync();
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected sealed override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var success = BoolResult.Success;

            // Checking only the components that were successfully started, because otherwise the shutdown might fail
            // due to the fact that invariants established by the startup are not met during shutdown.

            // Background operations need to stop before shutting down the component
            foreach (var nestedComponent in _shutdownEligibleComponents.Where(n => n is BackgroundOperation))
            {
                success &= await nestedComponent.ShutdownAsync(context);
            }

            success &= await ShutdownComponentAsync(context);

            foreach (var nestedComponent in _shutdownEligibleComponents.Where(n => n is not BackgroundOperation))
            {
                success &= await nestedComponent.ShutdownAsync(context);
            }

            return success;
        }

        /// <summary>
        /// Component startup which runs after all dependent components but before any background operations
        /// </summary>
        protected virtual Task<BoolResult> StartupComponentAsync(OperationContext context)
        {
            return BoolResult.SuccessTask;
        }

        /// <summary>
        /// Component shutdown which runs after all background operations but before any dependent components
        /// </summary>
        protected virtual Task<BoolResult> ShutdownComponentAsync(OperationContext context)
        {
            return BoolResult.SuccessTask;
        }
    }
}
