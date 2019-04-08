// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Base class for facilitating running services.
    /// </summary>
    public class ServiceManager
    {
        /// <summary>
        /// Default ServiceManager which only works for pips that have no service dependencies.
        /// </summary>
        public static readonly ServiceManager Default = new ServiceManager();

        /// <summary>
        /// Executes the service dependencies for the given process
        /// </summary>
        public virtual Task<bool> TryRunServiceDependenciesAsync(
            IPipExecutionEnvironment environment,
            IEnumerable<PipId> servicePips,
            LoggingContext loggingContext)
        {
            // NOTE: This contract assertion cannot be a precondition because it only applies to the default service manager
            Contract.Assert(
                !servicePips.Any(),
                "Default service manager cannot be used to execute services and thus should not be called with processes requires service pip execution");

            return BoolTask.True;
        }
    }
}
