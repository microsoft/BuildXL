// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Utils.Internal
{
    /// <summary>
    /// Shim for classes which implemented <see cref="StartupShutdownSlimBase"/> or <see cref="StartupShutdownBase"/>
    /// allow functionality from <see cref="StartupShutdownComponentBase"/> without changing API. (i.e. implementing
    /// <see cref="StartupComponentAsync"/> instead of <see cref="StartupCoreAsync"/>)
    /// </summary>
    public abstract class StartupShutdownComponentBaseShim : StartupShutdownComponentBase
    {
        protected virtual new Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            return BoolResult.SuccessTask;
        }

        protected virtual new Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            return BoolResult.SuccessTask;
        }

        protected sealed override Task<BoolResult> StartupComponentAsync(OperationContext context)
        {
            return StartupCoreAsync(context);
        }

        protected sealed override Task<BoolResult> ShutdownComponentAsync(OperationContext context)
        {
            return ShutdownCoreAsync(context);
        }
    }
}
