// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Allows running an operation in the background as a <see cref="IStartupShutdownSlim"/>
    /// </summary>
    public class BackgroundOperation : StartupShutdownSlimBase
    {
        private readonly Func<OperationContext, Task<BoolResult>> _operation;

        private Task<BoolResult>? _operationTask;

        protected override Tracer Tracer { get; }

        public BackgroundOperation(string name, Func<OperationContext, Task<BoolResult>> operation)
        {
            Tracer = new Tracer(name);
            _operation = operation;
        }

        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            _operationTask = Task.Run(() => _operation(context));
            return base.StartupCoreAsync(context);
        }

        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            if (_operationTask != null)
            {
                return _operationTask;
            }

            return BoolResult.SuccessTask;
        }
    }

}
